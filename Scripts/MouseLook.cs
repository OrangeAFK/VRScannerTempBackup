using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float turnSpeed = 90f;

    [SerializeField] float headUpperAngleLimit = 85f;
    [SerializeField] float headLowerAngleLimit = -80f;

    float yaw = 0f, pitch = 0f;

    Quaternion bodyStartOrientation;
    Quaternion headStartOrientation;

    Transform head;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        head = GetComponentInChildren<Camera>().transform;

        bodyStartOrientation = transform.localRotation;
        headStartOrientation = head.transform.localRotation;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update is called once per frame
    void Update()
    {
        var horizontal = Input.GetAxis("Mouse X") * Time.deltaTime * turnSpeed;
        var vertical = Input.GetAxis("Mouse Y") * Time.deltaTime * turnSpeed;

        yaw += horizontal;
        pitch += vertical;

        pitch = Mathf.Clamp(pitch, headLowerAngleLimit, headUpperAngleLimit);

        var bodyRotation = Quaternion.AngleAxis(yaw, Vector3.up);
        var headRotation = Quaternion.AngleAxis(pitch, Vector3.right);

        transform.localRotation = bodyRotation * bodyStartOrientation;
        head.localRotation = headRotation * headStartOrientation;
    }
}
