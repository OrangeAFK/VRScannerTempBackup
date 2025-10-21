using UnityEngine;

public class TabletRotation : MonoBehaviour
{

    public float rotationSpeed = 90f;
    public float maxRotation = 45f, minRotation = -45f;

    private Quaternion baseOrientation;
    private float pitch = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        baseOrientation = transform.localRotation;
    }

    // Update is called once per frame
    void Update()
    {
        float rotateX = 0f;
        if (Input.GetKey(KeyCode.UpArrow) && rotateX > minRotation)
        {
            rotateX = -1f;
        }
        else if (Input.GetKey(KeyCode.DownArrow) && rotateX < maxRotation)
        {
            rotateX = 1f;
        }

        pitch += rotateX * Time.deltaTime * rotationSpeed;
        pitch = Mathf.Clamp(pitch, minRotation, maxRotation);

        Quaternion rotation = Quaternion.AngleAxis(pitch, Vector3.right);

        transform.localRotation = rotation * baseOrientation;
    }
}
