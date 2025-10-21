using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class Scanner : MonoBehaviour
{
    [Header("Scan Settings")]
    public Camera scanCamera;
    public GameObject pointPrefab;
    public int resolutionX = 60, resolutionY = 40;
    public float scanRange;
    public LayerMask scanLayerMask;
    public bool showPoints = true;
    public bool debugMsgs = false;

    [Header("Pain Points Settings")]
    public bool usePainPoints = true;
    [Range(0, 1)] public float noise = .01f;
    [Range(0, 1)] public float dropoutChance = .1f;
    public float angleTolerance = 75f;
    public float stableSpeedThreshold = .015f;
    // drift settings
    public Transform scannerTransform;
    public float maxDriftRate = .05f;

    private Vector3 lastScannerPosition, scannerVelocity;
    public Vector3 drift; // make private when not debugging
    
    [HideInInspector]
    public List<Vector3> pointCloud = new List<Vector3>();
    private List<GameObject> spawnedPoints = new List<GameObject>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        scanRange = scanCamera.farClipPlane;
        lastScannerPosition = scannerTransform.position;
    }

    // Update is called once per frame
    void Update()
    {
        // track velocity
        Vector3 curPos = scannerTransform.position;
        scannerVelocity = (curPos - lastScannerPosition) / Time.deltaTime;
        lastScannerPosition = curPos;

        // update drift
        if (usePainPoints)
        {
            float speed = scannerVelocity.magnitude;
            float driftAmount = Mathf.Clamp01(speed / 2f) * maxDriftRate * Time.deltaTime;
            drift += Random.insideUnitSphere * driftAmount;
            if (speed < stableSpeedThreshold)
            {
                drift = Vector3.Lerp(drift, Vector3.zero, Time.deltaTime*0.5f); // take about 2 seconds to restabilize
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            PerformScan();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearPoints();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            showPoints = !showPoints;
            foreach (var pt in spawnedPoints)
            {
                pt.SetActive(showPoints);
            }
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            ExportToCSV();
        }
    }

    void PerformScan()
    {
        pointCloud.Clear();

        // based on resolution x and y values, raycast left-to-right, top-to-bottom
        // evenly across the camera frustum
        for (int y = 0; y < resolutionY; ++y)
        {
            for (int x = 0; x < resolutionX; ++x)
            {
                float u = (x + 0.5f) / resolutionX;
                float v = (y + 0.5f) / resolutionY;

                Ray ray = scanCamera.ViewportPointToRay(new Vector3(u, v, 0f));
                if (Physics.Raycast(ray, out RaycastHit hit, scanRange, scanLayerMask))
                {
                    if (usePainPoints)
                    {

                        // reject sharp angles
                        float angle = Vector3.Angle(hit.normal, -ray.direction);
                        //Debug.Log($"{angle} <= {angleTolerance}");
                        if (angle > angleTolerance) continue;

                        // assume some rays randomly do not return to the scanner
                        float dropout = dropoutChance * Mathf.InverseLerp(0f, scanRange, hit.distance);
                        if (Random.value < dropout) continue;

                        // add depth noise and drift
                        Vector3 noisyPoint = hit.point + ray.direction.normalized * Random.Range(-noise, noise);
                        noisyPoint += drift;

                        pointCloud.Add(noisyPoint);
                        SpawnPointVisual(noisyPoint);
                    }
                    else
                    {
                        pointCloud.Add(hit.point);
                        SpawnPointVisual(hit.point);
                    }
                }
            }
        }

        if (debugMsgs)
        {
            Debug.Log($"[Scanner] scanned {pointCloud.Count} points.");
        }
    }

    void SpawnPointVisual(Vector3 pos)
    {
        if (pointPrefab != null)
        {
            GameObject pt = Instantiate(pointPrefab, pos, Quaternion.identity);
            pt.SetActive(showPoints);
            spawnedPoints.Add(pt);
        }
    }

    void ClearPoints()
    {
        foreach (GameObject obj in spawnedPoints)
        {
            Destroy(obj);
        }
        spawnedPoints.Clear();
        pointCloud.Clear();
    }

    public void ExportToCSV(string filename = "PointCloud.csv")
    {
        string path = Path.Combine(Application.persistentDataPath, filename);
        using (StreamWriter writer = new StreamWriter(path))
        {
            writer.WriteLine("x,y,z");
            foreach (Vector3 pt in pointCloud)
            {
                writer.WriteLine($"{pt.x},{pt.y},{pt.z}");
            }
        }

        if (debugMsgs)
        {
            Debug.Log($"[Scanner] exported point cloud to {path}");
        }
    }
}
