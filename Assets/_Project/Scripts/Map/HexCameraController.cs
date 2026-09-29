using UnityEngine;

namespace Runeterra.Map
{
    /// <summary>Камера стратегии: WASD/стрелки или средняя кнопка — панорама, колесо — зум, Q/E — поворот.</summary>
    public class HexCameraController : MonoBehaviour
    {
        public float panSpeed = 12f;
        public float rotateSpeed = 90f;
        public float zoomSpeed = 6f;
        public float minDistance = 4f;
        public float maxDistance = 40f;
        [Tooltip("Наклон камеры на минимальном и максимальном отдалении")]
        public float pitchNear = 38f;
        public float pitchFar = 62f;
        public float distance = 13f;

        private Vector3 _pivot;
        private float _yaw;
        private float _targetDistance = 13f;

        public void FocusOn(Vector3 point)
        {
            _pivot = point;
            _targetDistance = distance;
            Apply();
        }

        private void Update()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            var rot = Quaternion.Euler(0f, _yaw, 0f);
            float speedScale = distance / 13f;
            _pivot += rot * input.normalized * (panSpeed * speedScale * Time.deltaTime);

            if (Input.GetMouseButton(2))
                _pivot -= rot * new Vector3(Input.GetAxis("Mouse X"), 0f, Input.GetAxis("Mouse Y")) * speedScale;

            if (Input.GetKey(KeyCode.Q)) _yaw += rotateSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) _yaw -= rotateSpeed * Time.deltaTime;

            _targetDistance = Mathf.Clamp(_targetDistance - Input.mouseScrollDelta.y * zoomSpeed * speedScale * 0.5f, minDistance, maxDistance);
            distance = Mathf.Lerp(distance, _targetDistance, 1f - Mathf.Exp(-10f * Time.deltaTime));
            Apply();
        }

        private void Apply()
        {
            float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
            var rot = Quaternion.Euler(Mathf.Lerp(pitchNear, pitchFar, t), _yaw, 0f);
            transform.SetPositionAndRotation(_pivot - rot * Vector3.forward * distance, rot);
        }
    }
}
