using UnityEngine;

public class Movement : MonoBehaviour
{
    [SerializeField] float moveSpeed = 6f;

    Vector3 moveDirection = Vector3.zero;
    CharacterController controller;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        float moveX = 0f;
        float moveZ = 0f;

        // WASD input only
        if (Input.GetKey(KeyCode.W))
            moveZ += 1f;
        if (Input.GetKey(KeyCode.S))
            moveZ -= 1f;
        if (Input.GetKey(KeyCode.A))
            moveX -= 1f;
        if (Input.GetKey(KeyCode.D))
            moveX += 1f;

        Vector3 input = new Vector3(moveX, 0f, moveZ).normalized;
        input *= moveSpeed;

        moveDirection = transform.TransformDirection(input);

        controller.Move(moveDirection * Time.deltaTime);
    }
}
