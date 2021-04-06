using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Spectrogram {
    /// <summary>
    /// Does all the spectrogram fanciness.
    /// TODO: Move the UI stuff into its own dedicated script.
    /// </summary>
    public class SpectrogramRenderer : MonoBehaviour {
        [SerializeField] private SpectrogramQuality _quality = SpectrogramQuality.Medium;
        [SerializeField] private ComputeShader _compute;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private RawImage _renderer;
        [SerializeField] private Gradient _gradient;
        [SerializeField] private bool _stereo;

        [Header("UI Stuffs")] 
        [SerializeField] private Rect _viewRect = new Rect(0f, 0f, 1f, 1f);
        [SerializeField] private Vector2 _minSize = new Vector2(0.1f, 0.1f);
        [SerializeField] private Text _timeText;
        [SerializeField] private Text _freqText;
        [SerializeField] private Image _timeScale;
        [SerializeField] private Image _freqScale;
        [SerializeField] private Text _stats;
        
        // State/config.
        private bool _stereoMode;
        private int _sampleCount;
        private bool _error;
        
        // Textures.
        private Texture2D _gradientTexture;
        private RenderTexture _previousTexture;
        private RenderTexture _currentTexture;
        
        // Buffers.
        private float[] _samplesLeft;
        private float[] _samplesRight;
        private float[] _samplesFinal;
        private ComputeBuffer _samplesBuffer;

        // Stats stuff.
        private Stopwatch _specTimer;
        private FloatRingBuffer _specAverage;
        private FloatRingBuffer _dtAverage;
        private string _sampleRate = "";
        private string _bufferSize = "";
        
        private Vector3 _mousePrevious;
        
        // Shader property IDs.
        private static readonly int _idSampleCount = Shader.PropertyToID("SampleCount");
        private static readonly int _idSamples = Shader.PropertyToID("Samples");
        private static readonly int _idPrevious = Shader.PropertyToID("Previous");
        private static readonly int _idCurrent = Shader.PropertyToID("Current");

        private void Start() {
            _stereoMode = _stereo;
            
            // Get sample count from quality setting.
            // Prevents user from entering invalid values.
            _sampleCount = _quality switch {
                SpectrogramQuality.VeryLow => 256,
                SpectrogramQuality.Low => 512,
                SpectrogramQuality.Medium => 1024,
                SpectrogramQuality.High => 2048,
                SpectrogramQuality.VeryHigh => 4096,
                SpectrogramQuality.Extreme => 8192,
                _ => -1
            };
            
            // Catch some basic errors.
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogError("Your system does not support compute shaders.\n" +
                               "The spectrogram cannot run on the current platform.");
                _error = true;
                return;
            }
            
            if (_sampleCount == -1) {
                Debug.LogError("Invalid quality level selected.\n" +
                               "Make sure a valid level is selected in the inspector and restart.");
                _error = true;
                return;
            }
            
            if (SystemInfo.maxTextureSize < _sampleCount) {
                Debug.LogError($"Your system cannot support the selected quality level.\n" +
                               $"Try selecting a lower quality level and restarting.");
                _error = true;
                return;
            }
            
            // Create the two render textures for the spectrogram.
            _previousTexture = new RenderTexture(_sampleCount, _sampleCount, 1, RenderTextureFormat.ARGB32, 1) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            
            _currentTexture = new RenderTexture(_sampleCount, _sampleCount, 1, RenderTextureFormat.ARGB32, 1) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            _previousTexture.Create();
            _currentTexture.Create();
            
            // Generate a 1D LUT from the gradient.
            _gradientTexture = TextureUtility.GenerateGradientTexture(_gradient);
            
            // Configure the renderer with the spectrogram and gradient textures.
            _renderer.material.SetTexture("_GradientTex", _gradientTexture);
            _renderer.texture = _currentTexture;
            
            // Initialize sample buffers.
            _samplesLeft = new float[_stereoMode ? _sampleCount / 2 : _sampleCount];
            _samplesRight = new float[_stereoMode ? _sampleCount / 2 : _sampleCount];
            _samplesFinal = new float[_sampleCount];
            _samplesBuffer = new ComputeBuffer(_sampleCount, 4, ComputeBufferType.Structured);
            
            // Generate static UI strings.
            _sampleRate = $"Sample Rate: {AudioSettings.outputSampleRate:N0}Hz\n";
            _bufferSize = $"Sample Buffer: {(float)(_sampleCount * 4) / 1024:N0}kB\n";

            _specTimer = new Stopwatch();
            _specAverage = new FloatRingBuffer(120);
            _dtAverage = new FloatRingBuffer(120);
        }
        
        /// <summary>
        /// Gets the normalized position of the mouse within the spectrogram view.
        /// </summary>
        /// <returns></returns>
        private Vector2 GetMouseViewPosition() {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_renderer.rectTransform, Input.mousePosition, null, out var mouseLocal);
            var rectSize = _renderer.rectTransform.rect.size;
            mouseLocal.x = Mathf.Clamp01(mouseLocal.x / rectSize.x);
            mouseLocal.y = Mathf.Clamp01(mouseLocal.y / rectSize.y);

            return mouseLocal;
        }
        
        /// <summary>
        /// We run the spectrogram's loop in FixedUpdate to keep it framerate independent.
        /// </summary>
        private void FixedUpdate() {
            // No need to run if the source isn't playing.
            if (_error || !_audioSource.isPlaying) return;
            
            // Start profiling.
            _specTimer.Start();
            Profiler.BeginSample("Spectrogram Pipeline");

            // Grab samples for both channels.
            _audioSource.GetSpectrumData(_samplesLeft, 0, FFTWindow.Hamming);
            _audioSource.GetSpectrumData(_samplesRight, 1, FFTWindow.Hamming);

            // Write channels in sequentially for stereo mode.
            // Mono mode just averages the channels.
            if (_stereoMode) {
                Array.Copy(_samplesLeft, 0, _samplesFinal, 0, _samplesLeft.Length);
                Array.Copy(_samplesRight, 0, _samplesFinal, _samplesFinal.Length / 2, _samplesRight.Length);
            }
            else {
                for (var i = 0; i < _samplesFinal.Length; i++) {
                    _samplesFinal[i] = (_samplesLeft[i] + _samplesRight[i]) * 0.5f;
                }
            }
                
            // Upload latest sample data and dispatch the compute shader.
            _samplesBuffer.SetData(_samplesFinal);
            _compute.SetInt(_idSampleCount, _sampleCount);
            _compute.SetBuffer(0, _idSamples, _samplesBuffer);
            _compute.SetTexture(0, _idPrevious, _previousTexture);
            _compute.SetTexture(0, _idCurrent, _currentTexture);
            _compute.Dispatch(0, _sampleCount / 32, _sampleCount / 32, 1);
                
            // Copy current texture into previous.
            Graphics.CopyTexture(_currentTexture, _previousTexture);
            
            // Done profiling.
            _specTimer.Stop();
            _specAverage.Push(_specTimer.ElapsedMilliseconds);
            _specTimer.Reset();
            Profiler.EndSample();
        }

        private void Update() {
            if (_error) return;
            
            // Update view rect.
            var currentOffset = _viewRect.min;
            var currentSize = _viewRect.size;
            
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

            // Clamp and set the view rect.
            currentSize.x = Mathf.Clamp(currentSize.x, _minSize.x, 1.0f);
            currentSize.y = Mathf.Clamp(currentSize.y, _minSize.y, 1.0f);
            currentOffset.x = Mathf.Clamp(currentOffset.x, 0f, 1.0f - currentSize.x);
            currentOffset.y = Mathf.Clamp(currentOffset.y, 0f, 1.0f - currentSize.y);

            _viewRect.min = currentOffset;
            _viewRect.size = currentSize;
            _renderer.uvRect = _viewRect;
            
            // Update stat display values.
            var timeEnd = Mathf.RoundToInt((_sampleCount * Time.fixedDeltaTime) * currentSize.x);
            var freqEnd = Mathf.RoundToInt((AudioSettings.outputSampleRate / 2000f) * currentSize.y);
            _timeText.text = $"{timeEnd:n0}s";
            _freqText.text = $"{freqEnd:n0}K";

            var timeTransform = _timeScale.rectTransform;
            timeTransform.anchoredPosition = new Vector2(currentOffset.x * timeTransform.rect.width, 0f);
            timeTransform.localScale = new Vector3(currentSize.x, 1f, 1f);
            
            var freqTransform = _freqScale.rectTransform;
            freqTransform.anchoredPosition = new Vector2(0f, currentOffset.y * freqTransform.rect.height);
            freqTransform.localScale = new Vector3(1f, currentSize.y, 1f);

            _dtAverage.Push(Time.deltaTime);
            var specAvg = _specAverage.Average();
            var dtAvg = _dtAverage.Average();
            
            _stats.text = _sampleRate +
                          _bufferSize +
                          $"FFT Length: {_sampleCount:N0}\n" +
                          $"Spectrogram Time: {specAvg:n1}ms\n" +
                          $"Total Time: {dtAvg * 1000:n1}ms\n" +
                          $"FPS: {1f / dtAvg:n0}";

            _mousePrevious = Input.mousePosition;
        }

        private void OnDestroy() {
            // Cleanup render texture and buffer.
            _previousTexture.Release();
            _currentTexture.Release();
            _samplesBuffer.Dispose();
        }
    }
}