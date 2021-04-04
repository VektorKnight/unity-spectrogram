using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Spectrogram {
    /// <summary>
    /// Wraps a regular old ComputeBuffer with some very simple ring buffer logic.
    /// Floats pushed into the buffer are converted to 16-bit UNORMS which are then packed into a single UInt32.
    /// This cuts the memory usage of the buffer in half while still retaining plenty of precision.
    /// I did try using 8-bit UNORMS but the loss of precision wasn't within an acceptable level.
    /// </summary>
    public class SpectrogramBuffer : IDisposable {
        // Internal length and data buffer.
        private readonly int _lengthInternal;
        private readonly uint[] _data;
        
        // Props.
        public int Length { get; }
        public ComputeBuffer CBuffer { get; }
        public int WriteIndex { get; private set; }

        /// <summary>
        /// Creates a new spectrogram buffer with a given width and height.
        /// </summary>
        /// <param name="width">Width of the final output texture.</param>
        /// <param name="height">Height of the final output texture.</param>
        public SpectrogramBuffer(int width, int height) {
            // User-facing length is just width x height.
            Length = width * height;
            
            // Internal length uses 1/2 the width since two values are packed into a UInt32.
            _lengthInternal = (width / 2) * height;
            _data = new uint[_lengthInternal];
            
            // The buffer mode must be SubUpdates for the UploadData() function to work properly unsafe is used.
            // If you decide to remove the unsafe code, be sure to remove the mode parameter as well.
            CBuffer = new ComputeBuffer(_lengthInternal, 4, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        }
        
        /// <summary>
        /// Push a set of values into the buffer.
        /// This should be the data from your FFT.
        /// </summary>
        public void PushRange(float[] values) {
            for (var i = 0; i < values.Length; i += 2) {
                // Pack floats in two at a time into the upper and lower 16-bits.
                uint value = (uint)Mathf.RoundToInt(values[i] * ushort.MaxValue);
                value += (uint) Mathf.RoundToInt(values[i + 1] * ushort.MaxValue) << 16;
                
                // Set packed value and increment the ring write index.
                _data[WriteIndex] = value;
                WriteIndex = (WriteIndex + 1) % _lengthInternal;
            }
        }
        
        /// <summary>
        /// Uploads the current contents of the buffer to the GPU for processing.
        /// Call this once you have pushed a new set of values from our FFT or other source.
        /// </summary>
        public void UploadData() {
            // Ran into some weird performance issues here.
            // For smaller buffers, aka N=2048 or below, SetData() was faster.
            // Anything above and it imploded being almost 3x slower than the code below.
            // If you're not a fan of unsafe or don't want higher resolutions, just comment it out.
            // Don't forget to uncomment the regular SetData call as well.
            unsafe {
                var temp = CBuffer.BeginWrite<uint>(0, _lengthInternal);
                
                var destPtr = temp.GetUnsafePtr();

                fixed (void* srcPtr = _data) {
                    Buffer.MemoryCopy(srcPtr, destPtr, _lengthInternal * 4, _lengthInternal * 4);
                }
                
                CBuffer.EndWrite<uint>(_lengthInternal);
            }
            
            //_cBuffer.SetData(_data);
        }
        
        /// <summary>
        /// Disposes of the internal compute buffer.
        /// </summary>
        public void Dispose() {
            CBuffer?.Dispose();
        }
    }
}