using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    Rigidbody _playerRB;
    Vector3 _moveDirection;
    float _moveSpeed = 15f;
    float Turnspeed = 20f;

    [SerializeField]
    Camera primaryCam = null;
    [SerializeField]
    Camera secondaryCam = null;

    void Start()
    {
        _playerRB = GetComponent<Rigidbody>();
        primaryCam.enabled = true;
        secondaryCam.enabled = false;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))// changes camera view
        {
            primaryCam.enabled = !primaryCam.enabled;
            secondaryCam.enabled = !secondaryCam.enabled;
        }
        float forw = Input.GetAxis("Vertical");
        float side = -Input.GetAxis("Horizontal");
        if (Input.GetKey("space"))
        {
            _moveDirection = transform.up;
        }
        else if (Input.GetKey("c"))
        {
            _moveDirection = -transform.up;
        }
        else
        {
            _moveDirection = Vector3.zero;
        }

        _playerRB.velocity = _moveDirection * _moveSpeed / 2 + (transform.forward * side * _moveSpeed) + (transform.right * forw * _moveSpeed);

        if (Input.GetMouseButton(1)) //right mouse button held down allow camera rotation
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            float pitch = Input.GetAxis("Mouse Y") * 1f * Turnspeed * 10 * Time.deltaTime;
            this.gameObject.transform.Rotate(pitch * Vector3.forward, Space.Self);

            float yaw = Input.GetAxis("Mouse X") * Turnspeed * 10 * Time.deltaTime;
            this.gameObject.transform.Rotate(yaw * Vector3.up, Space.World);

            transform.eulerAngles = new Vector3(0, Mathf.Clamp(transform.eulerAngles.y, 0, 360), Mathf.Clamp((transform.eulerAngles.z <= 180) ? transform.eulerAngles.z : -(360 - transform.eulerAngles.z), -90, 90));
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }      
    }
}
