﻿using System;
using System.Diagnostics;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Audio.Utility;

/// <summary>
///     A very basic circular buffer implementation based off NAudio's Buffer
/// </summary>
public class CircularFloatBuffer
{
    private readonly float[] buffer;
    private readonly object lockObject;
    private int byteCount;
    private int readPosition;
    private int writePosition;

    /// <summary>
    ///     Create a new circular buffer
    /// </summary>
    /// <param name="size">Max buffer size in bytes</param>
    public CircularFloatBuffer(int size)
    {
        buffer = new float[size];
        lockObject = new object();
    }

    /// <summary>
    ///     Maximum length of this circular buffer
    /// </summary>
    public int MaxLength => buffer.Length;

    /// <summary>
    ///     Number of bytes currently stored in the circular buffer
    /// </summary>
    public int Count
    {
        get
        {
            lock (lockObject)
            {
                return byteCount;
            }
        }
    }

    /// <summary>
    ///     Write data to the buffer
    /// </summary>
    /// <param name="data">Data to write</param>
    /// <param name="offset">Offset into data</param>
    /// <param name="count">Number of bytes to write</param>
    /// <returns>number of bytes written</returns>
    public int Write(float[] data, int offset, int count)
    {
        lock (lockObject)
        {
            var bytesWritten = 0;
            if (count > buffer.Length - byteCount) count = buffer.Length - byteCount;
            // write to end
            var writeToEnd = Math.Min(buffer.Length - writePosition, count);
            Array.Copy(data, offset, buffer, writePosition, writeToEnd);
            writePosition += writeToEnd;
            writePosition %= buffer.Length;
            bytesWritten += writeToEnd;
            if (bytesWritten < count)
            {
                Debug.Assert(writePosition == 0);
                // must have wrapped round. Write to start
                Array.Copy(data, offset + bytesWritten, buffer, writePosition, count - bytesWritten);
                writePosition += count - bytesWritten;
                bytesWritten = count;
            }

            byteCount += bytesWritten;
            return bytesWritten;
        }
    }

    /// <summary>
    ///     Read from the buffer
    /// </summary>
    /// <param name="data">Buffer to read into</param>
    /// <param name="offset">Offset into read buffer</param>
    /// <param name="count">Bytes to read</param>
    /// <returns>Number of bytes actually read</returns>
    public int Read(float[] data, int offset, int count)
    {
        lock (lockObject)
        {
            if (count > byteCount) count = byteCount;
            var bytesRead = 0;
            var readToEnd = Math.Min(buffer.Length - readPosition, count);
            Array.Copy(buffer, readPosition, data, offset, readToEnd);
            bytesRead += readToEnd;
            readPosition += readToEnd;
            readPosition %= buffer.Length;

            if (bytesRead < count)
            {
                // must have wrapped round. Read from start
                Debug.Assert(readPosition == 0);
                Array.Copy(buffer, readPosition, data, offset + bytesRead, count - bytesRead);
                readPosition += count - bytesRead;
                bytesRead = count;
            }

            byteCount -= bytesRead;
            Debug.Assert(byteCount >= 0);
            return bytesRead;
        }
    }

    /// <summary>
    ///     Resets the buffer
    /// </summary>
    public void Reset()
    {
        lock (lockObject)
        {
            ResetInner();
        }
    }

    private void ResetInner()
    {
        byteCount = 0;
        readPosition = 0;
        writePosition = 0;
    }

    /// <summary>
    ///     Advances the buffer, discarding bytes
    /// </summary>
    /// <param name="count">Bytes to advance</param>
    public void Advance(int count)
    {
        lock (lockObject)
        {
            if (count >= byteCount)
            {
                ResetInner();
            }
            else
            {
                byteCount -= count;
                readPosition += count;
                readPosition %= MaxLength;
            }
        }
    }
}