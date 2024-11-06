using UnityEngine;

[ExecuteAlways]
public class AlwaysFaceCamera : MonoBehaviour {
    public Camera targetCamera;

    private void Update() {
        if(targetCamera != null) {
            transform.LookAt(targetCamera.transform.position);
        }
    }
}
