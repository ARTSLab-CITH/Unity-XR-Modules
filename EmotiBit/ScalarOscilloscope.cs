using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

namespace EmotiBit
{
    [RequireComponent(typeof(LineRenderer))]
    public class ScalarOscilloscope : MonoBehaviour
    {
        [Header("UI References")]
        public Text label;
        public Text minLabel;
        public Text maxLabel;
        public Text currentLabel;

        [Header("Settings")]
        public float timeWindow = 5f; // seconds
        public int maxPoints = 25;
        public bool isDemo = false;

        private class DataPoint
        {
            public float Timestamp;
            public float Value;
        }

        private LineRenderer _lineRenderer;
        private List<DataPoint> _dataPoints = new List<DataPoint>();
        private float _minVal = float.MaxValue;
        private float _maxVal = float.MaxValue;

        void OnValidate()
        {
            if (Application.isEditor)
            {
                Awake();
                if (isDemo)
                {
                    GenerateDemoData();
                }
                else
                {
                    _dataPoints.Clear();
                }
                UpdateGraph();
            }
        }

        void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer != null)
            {
                _lineRenderer.positionCount = 0;
            }
        }

        void GenerateDemoData()
        {
            _dataPoints.Clear();
            int numPoints = 100;
            float now = Time.time;
            for (int i = 0; i < numPoints; i++)
            {
                float t = i / (float)(numPoints - 1);
                _dataPoints.Add(new DataPoint
                {
                    Timestamp = now - (timeWindow * (1f - t)),
                    Value = Mathf.Sin(t * Mathf.PI * 4f) // Two sine wave cycles
                });
            }
        }

        public void AddDataPoint(float value)
        {
            if (isDemo) return;

            _dataPoints.Add(new DataPoint { Timestamp = Time.time, Value = value });

            // Remove old data points that are outside the time window
            while (_dataPoints.Count > 0 && (Time.time - _dataPoints[0].Timestamp) > timeWindow)
            {
                _dataPoints.RemoveAt(0);
            }

            // Enforce max points limit
            while (_dataPoints.Count > maxPoints)
            {
                _dataPoints.RemoveAt(0);
            }

            UpdateGraph();
        }

        void UpdateGraph()
        {
            if (_lineRenderer == null) return;

            if (_dataPoints.Count < 2)
            {
                _lineRenderer.positionCount = 0;
                if (minLabel) minLabel.text = "(MIN)";
                if (maxLabel) maxLabel.text = "(MAX)";
                if (currentLabel) currentLabel.text = "(CURR)";
                return;
            }

            _minVal = float.MaxValue;
            _maxVal = float.MinValue;
            foreach (var p in _dataPoints)
            {
                if (p.Value < _minVal) _minVal = p.Value;
                if (p.Value > _maxVal) _maxVal = p.Value;
            }

            if (minLabel) minLabel.text = $"{_minVal:F2}";
            if (maxLabel) maxLabel.text = $"{_maxVal:F2}";
            if (currentLabel) currentLabel.text = $"{_dataPoints[_dataPoints.Count - 1].Value:F2}";

            // If min and max are the same, add a small buffer to avoid division by zero
            if (_minVal == _maxVal)
            {
                _maxVal += 0.1f;
            }

            _lineRenderer.positionCount = _dataPoints.Count;
            var rect = GetComponent<RectTransform>().rect;
            float now = Time.time;

            for (int i = 0; i < _dataPoints.Count; i++)
            {
                float timeOffset = now - _dataPoints[i].Timestamp;
                float x = rect.width * (1f - (timeOffset / timeWindow));
                float y = rect.height * Mathf.InverseLerp(_minVal, _maxVal, _dataPoints[i].Value);
                // important: leave space for data title
                y = 0.8f * y + 0.2f;
                _lineRenderer.SetPosition(i, new Vector3(x, y, 0));
            }
        }

        public void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
