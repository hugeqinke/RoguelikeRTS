using UnityEngine;
using UnityEngine.InputSystem;

public class CustomCamera : MonoBehaviour
{
    public float CameraMoveDelta;
    public float CameraRotateSpeed;
    public float CameraScrollSpeed;

    private Camera _camera;

    // Start is called before the first frame update
    void Start()
    {
        _camera = Camera.main;
    }

    // Update is called once per frame
    void Update()
    {
        MoveCamera();
        RotateCamera();
        ScrollCamera();
    }

    private void ScrollCamera()
    {
        var delta = Mouse.current.scroll.ReadValue().y;
        _camera.transform.position += delta * CameraScrollSpeed * transform.forward;
    }

    private void RotateCamera()
    {
        if (Mouse.current.middleButton.isPressed)
        {
            var delta = Mouse.current.delta.ReadValue();

            // horizontal rotation
            var angles = _camera.transform.rotation.eulerAngles;
            angles.y += delta.x * CameraRotateSpeed;

            // vertical rotation
            if (angles.x > 180)
            {
                angles.x -= 360;
            }
            angles.x -= delta.y * CameraRotateSpeed;
            angles.x = Mathf.Clamp(angles.x, -90, 90);

            _camera.transform.rotation = Quaternion.Euler(angles);
        }
    }

    private void MoveCamera()
    {
        var moveDelta = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
        {
            var forwardDir = Quaternion.Euler(0, _camera.transform.eulerAngles.y, 0) * Vector3.forward;
            moveDelta = forwardDir * CameraMoveDelta;
        }
        if (Keyboard.current.sKey.isPressed)
        {
            var backwardDir = Quaternion.Euler(0, _camera.transform.eulerAngles.y, 0) * Vector3.back;
            moveDelta = backwardDir * CameraMoveDelta;
        }
        if (Keyboard.current.aKey.isPressed)
        {
            var leftDir = Quaternion.Euler(0, _camera.transform.eulerAngles.y, 0) * Vector3.left;
            moveDelta += leftDir * CameraMoveDelta;
        }
        if (Keyboard.current.dKey.isPressed)
        {
            var rightDir = Quaternion.Euler(0, _camera.transform.eulerAngles.y, 0) * Vector3.right;
            moveDelta += rightDir * CameraMoveDelta;
        }

        _camera.transform.position += moveDelta;
    }
}

