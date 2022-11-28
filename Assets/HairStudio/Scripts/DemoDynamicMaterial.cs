using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HairStudio {
    public class DemoDynamicMaterial : MonoBehaviour
    {
        public float speed, min, max;

        void Update() {
            var rate = Mathf.Sin(Time.time * speed);
            rate = rate / 2 + 0.5f;
            GetComponent<HairRenderer>().material.SetFloat(Shader.PropertyToID("_ThicknessRoot"), Mathf.Lerp(min, max, rate));
        }
    }
}