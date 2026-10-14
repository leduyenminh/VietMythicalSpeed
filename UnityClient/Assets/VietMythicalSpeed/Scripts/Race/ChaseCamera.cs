using UnityEngine;

namespace VMS.Race
{
    /// <summary>Third-person camera that rides the track slightly behind the vessel, leaning into curves.</summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        public const float BaseFov = 62f;

        Camera _camera;
        float _punch;
        float _shake;
        Vector3 _velocity;

        public Camera Camera => _camera;

        public void Init(Camera camera)
        {
            _camera = camera;
            _camera.fieldOfView = BaseFov;
        }

        public void Punch(float fov) => _punch = Mathf.Max(_punch, fov);

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        public void Follow(TrackPath path, float songTime, float lateral, float speedNorm, float dt)
        {
            Vector3 behindPos;
            Quaternion behindRot;
            path.Sample(songTime - 0.2f, out behindPos, out behindRot);
            Vector3 target = behindPos + behindRot * new Vector3(lateral * 0.55f, 3.3f, -1.5f);

            Vector3 lookPos;
            Quaternion lookRot;
            path.Sample(songTime + 0.5f, out lookPos, out lookRot);
            Vector3 look = lookPos + lookRot * new Vector3(lateral * 0.75f, 0.9f, 0f);

            Vector3 position = Vector3.SmoothDamp(transform.position, target, ref _velocity, 0.04f, Mathf.Infinity, dt);
            if ((position - target).sqrMagnitude > 400f) position = target;

            if (_shake > 0f)
            {
                position += Random.insideUnitSphere * _shake * 0.3f;
                _shake = Mathf.Max(0f, _shake - dt * 3f);
            }

            Vector3 up = Vector3.Slerp(Vector3.up, behindRot * Vector3.up, 0.55f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, up));

            _punch = Mathf.Max(0f, _punch - dt * 18f);
            _camera.fieldOfView = BaseFov + 9f * Mathf.Clamp01(speedNorm) + _punch;
        }
    }
}
