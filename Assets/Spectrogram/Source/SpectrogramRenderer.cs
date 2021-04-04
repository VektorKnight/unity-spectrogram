using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace Spectrogram {
    public class SpectrogramRenderer : MonoBehaviour {
        [SerializeField] private int _sampleCount = 4096;
        [SerializeField] private ComputeShader _compute;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private RawImage _renderer;
        [SerializeField] private Gradient _gradient;
        [SerializeField] private bool _stereo = false;

        [Header("UI Stuffs")] 
        [SerializeField] private Rect _viewRect = new Rect(0f, 0f, 1f, 1f);
        [SerializeField] private Vector2 _minSize = new Vector2(0.1f, 0.1f);
        [SerializeField] private Text _timeText;
        [SerializeField] private Text _freqText;
        [SerializeField] private Image _timeScale;
        [SerializeField] private Image _freqScale;
        [SerializeField] private Text _stats;
        
        // textures.
        private Texture2D _gradientTexture;
        private RenderTexture _spectrogramTex;
        
        // Buffers.
        private float[] _samplesLeft;
        private float[] _samplesRight;
        private float[] _samplesMono;
        private SpectrogramBuffer _spectrogramBuffer;
        
        // Stats stuff.
        private Stopwatch _stopWatch;
        private string _sampleRate = "";
        private string _bufferSize = "";
        
        private Vector3 _mousePrevious;
        
        // Shader property IDs.
        private static readonly int _idCount = Shader.PropertyToID("SampleCount");
        private static readonly int _idRingIndex = Shader.PropertyToID("RingIndex");
        private static readonly int _idSamples = Shader.PropertyToID("Samples");
        private static readonly int _idResult = Shader.PropertyToID("Result");

        private void Start() {
            _spectrogramTex = new RenderTexture(_sampleCount, _sampleCount, 1, RenderTextureFormat.ARGB32, 1) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear
            };

            _gradientTexture = TextureUtility.GenerateGradientTexture(_gradient);
            _spectrogramTex.Create();
            
            _renderer.material.SetTexture("_GradientTex", _gradientTexture);
            _renderer.texture = _spectrogramTex;

            _samplesLeft = new float[_stereo ? _sampleCount / 2 : _sampleCount];
            _samplesRight = new float[_stereo ? _sampleCount / 2 : _sampleCount];
            _samplesMono = new float[_sampleCount];
            
            _spectrogramBuffer = new SpectrogramBuffer(_sampleCount, _sampleCount);

            _sampleRate = $"Sample Rate: {AudioSettings.outputSampleRate:N0}Hz\n";
            _bufferSize = $"Sample Buffer: {(float)(_spectrogramBuffer.Length * 2) / 1024:N0}kB\n";
            
            _stopWatch = new Stopwatch();

            Application.targetFrameRate = 60;
        }

        private Vector2 GetMouseViewPosition() {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_renderer.rectTransform, Input.mousePosition, null, out var mouseLocal);
            var rectSize = _renderer.rectTransform.rect.size;
            mouseLocal.x = Mathf.Clamp01(mouseLocal.x / rectSize.x);
            mouseLocal.y = Mathf.Clamp01(mouseLocal.y / rectSize.y);

            return mouseLocal;
        }

        private void Update() {
            if (_audioSource.isPlaying) {
                _stopWatch.Start();
                
                _audioSource.GetSpectrumData(_samplesLeft, 0, FFTWindow.Hamming);
                _audioSource.GetSpectrumData(_samplesRight, 1, FFTWindow.Hamming);

                if (_stereo) {
                    _spectrogramBuffer.PushRange(_samplesLeft);
                    _spectrogramBuffer.PushRange(_samplesRight);
                }
                else {
                    for (var i = 0; i < _samplesMono.Length; i++) {
                        _samplesMono[i] = (_samplesLeft[i] + _samplesRight[i]) * 0.5f;
                    }
                    
                    _spectrogramBuffer.PushRange(_samplesMono);
                }
                
                _spectrogramBuffer.UploadData();

                _compute.SetInt(_idCount, _sampleCount / 2);
                _compute.SetInt(_idRingIndex, _spectrogramBuffer.WriteIndex);
                _compute.SetBuffer(0, _idSamples, _spectrogramBuffer.CBuffer);
                _compute.SetTexture(0, _idResult, _spectrogramTex);
                _compute.Dispatch(0, _sampleCount / 32, _sampleCount / 32, 1);
                _stopWatch.Stop();
            }

            var currentOffset = _viewRect.min;
            var currentSize = _viewRect.size;
            
            // Update view rect.
            var scrollDelta = -Input.mouseScrollDelta.y * 0.01f;
            var panDelta = Input.mousePosition - _mousePrevious;
            panDelta.x = (panDelta.x / Screen.width) * currentSize.x;
            panDelta.y = (panDelta.y / Screen.height) * currentSize.y;

            var mouseLocal = GetMouseViewPosition();
            
            // Panning.
            if (Input.GetKey(KeyCode.Mouse0)) {
                currentOffset -= (Vector2)panDelta;
            }
            
            // Zooming.
            if (Input.GetKey(KeyCode.LeftShift)) {
                currentSize.x += scrollDelta;
            }
            else if (Input.GetKey(KeyCode.LeftControl)) {
                currentSize.y += scrollDelta;
            }
            else if (Mathf.Abs(scrollDelta) > float.Epsilon) {
                currentSize += new Vector2(scrollDelta, scrollDelta);
                
                // Center the zoom on the cursor.
                if (currentSize.x >= _minSize.x && currentSize.y >= _minSize.y) {
                    currentOffset -= new Vector2(scrollDelta * mouseLocal.x, scrollDelta * mouseLocal.y);
                }
            }
            
            // Reset view with home.
            if (Input.GetKeyDown(KeyCode.Home)) {
                currentSize = Vector2.one;
            }
            
            // Pause/play.
            if (Input.GetKeyDown(KeyCode.Space)) {
                if (!_audioSource.isPlaying) {
                    _audioSource.Play();
                }
                else {
                    _audioSource.Pause();
                }
            }

            // Clamp the view rect.
            currentSize.x = Mathf.Clamp(currentSize.x, _minSize.x, 1.0f);
            currentSize.y = Mathf.Clamp(currentSize.y, _minSize.y, 1.0f);
            currentOffset.x = Mathf.Clamp(currentOffset.x, 0f, 1.0f - currentSize.x);
            currentOffset.y = Mathf.Clamp(currentOffset.y, 0f, 1.0f - currentSize.y);

            _viewRect.min = currentOffset;
            _viewRect.size = currentSize;
            _renderer.uvRect = _viewRect;
            
            // Update display values.
            var timeEnd = Mathf.RoundToInt((_sampleCount / 60f) * currentSize.x);
            var freqEnd = Mathf.RoundToInt((AudioSettings.outputSampleRate / 2000f) * currentSize.y);
            _timeText.text = $"{timeEnd:n0}s";
            _freqText.text = $"{freqEnd:n0}K";

            var timeTransform = _timeScale.rectTransform;
            timeTransform.anchoredPosition = new Vector2(currentOffset.x * timeTransform.rect.width, 0f);
            timeTransform.localScale = new Vector3(currentSize.x, 1f, 1f);
            
            var freqTransform = _freqScale.rectTransform;
            freqTransform.anchoredPosition = new Vector2(0f, currentOffset.y * freqTransform.rect.height);
            freqTransform.localScale = new Vector3(1f, currentSize.y, 1f);


            var pipelineTime = _stopWatch.ElapsedMilliseconds;
            _stopWatch.Reset();
            
            _stats.text = _sampleRate +
                          _bufferSize +
                          $"FFT Length: {_sampleCount:N0}\n" +
                          $"Total Time: {pipelineTime}ms";

            _mousePrevious = Input.mousePosition;
        }

        private void OnDestroy() {
            // Cleanup render texture and buffer.
            _spectrogramTex.Release();
            _spectrogramBuffer.Dispose();
        }
    }
}