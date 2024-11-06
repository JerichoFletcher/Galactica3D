using UnityEngine;

public class CameraMove : MonoBehaviour {
    public Vector2 lookSpeed;
    public Vector3 moveSpeed;
    public float moveShiftModifier = 3f;

    private void RotateCamera() {
        Vector3 currRot = transform.rotation.eulerAngles;
        if(currRot.x > 180f) {
            currRot.x -= 360f;
        }
        float rotX = Input.GetAxis("Mouse Y");
        float rotY = Input.GetAxis("Mouse X");

        transform.rotation = Quaternion.Euler(
            Mathf.Clamp(currRot.x + rotX * lookSpeed.x * Time.deltaTime, -90f, 90f),
            currRot.y + rotY * lookSpeed.y * Time.deltaTime,
            0f
        );
    }

    private void Move() {
        float movX = Input.GetAxis("MoveX");
        float movY = Input.GetAxis("MoveY");
        float movZ = Input.GetAxis("MoveZ");

        Vector3 mov = new(movX * moveSpeed.x, movY * moveSpeed.y, movZ * moveSpeed.z);
        mov = transform.rotation * mov;
        if(Input.GetButton("Fire3")) {
            mov *= moveShiftModifier;
        }

        transform.position += mov * Time.deltaTime;
    }

    private void Update() {
        RotateCamera();
        Move();
    }
}
