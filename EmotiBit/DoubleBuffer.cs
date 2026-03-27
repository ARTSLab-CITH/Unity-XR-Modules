// DoubleBuffer.cs
using System.Collections.Generic;

public class DoubleBuffer<T>
{
    private List<T> _buffer0 = new List<T>();
    private List<T> _buffer1 = new List<T>();
    private List<T> _inputPtr;
    private List<T> _outputPtr;

    private readonly object _threadlock = new object();

    public DoubleBuffer()
    {
        _inputPtr = _buffer0;
        _outputPtr = _buffer1;
    }

    private void Swap()
    {
        lock (_threadlock)
        {
            var tempPtr = _inputPtr;
            _inputPtr = _outputPtr;
            _outputPtr = tempPtr;
            _inputPtr?.Clear();
        }
    }

    public void PushBack(T data)
    {
        if (_inputPtr != null)
        {
            lock (_threadlock)
            {
                _inputPtr.Add(data);
            }
        }
    }

    public void Get(ref List<T> output)
    {
        Swap();
        lock (_threadlock)
        {
            // Copying to the output list to avoid holding the lock
            // while the main thread processes the data.
            output.Clear();
            output.AddRange(_outputPtr);
        }
    }
}