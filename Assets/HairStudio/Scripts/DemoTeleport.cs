using UnityEngine;

namespace HairStudio {
    public class DemoTeleport : MonoBehaviour
    {
        private float cooldownRemaining;
        private bool lockHair = false;

        public float cooldown;
        public float offset;

        private void Awake() {
        }

        private void Start() {
            cooldownRemaining = cooldown;
        }

        void Update() {
            cooldownRemaining -= Time.deltaTime;
            if(cooldownRemaining < 0) {
                cooldownRemaining += cooldown;
                if (lockHair) {
                    transform.localPosition += Vector3.left * offset;
                    GetComponentInChildren<HairSimulation>().ResetHair();
                } else {
                    transform.localPosition += Vector3.right * offset;
                }
                lockHair = !lockHair;
            }
        }
    }
}