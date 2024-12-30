using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[BurstCompile]
struct TestJob : IJobParallelFor
{
    [ReadOnly] public NativeHashMap<int, int> ReadHash;
    public NativeHashMap<int, int>.ParallelWriter WriteHash;

    public void Execute(int index)
    {
        if (ReadHash.TryGetValue(index, out int value))
        {
            int v = value;
            v += 1;
            WriteHash.TryAdd(index, v);
        }
    }
}

public class Test : MonoBehaviour
{
    private NativeHashMap<int, int> _readHash;
    private NativeHashMap<int, int> _writeHash;

    private Dictionary<int, int> _test;

    private const int size = 100000;

    // Start is called before the first frame update
    void Start()
    {
        _test = new Dictionary<int, int>();
        // _readHash = new NativeHashMap<int, int>(size, Allocator.Persistent);
        // for (int i = 0; i < size; i++)
        // {
        //     _readHash.Add(i, 0);
        // }
    }

    // Update is called once per frame
    void Update()
    {
        // _writeHash = new NativeHashMap<int, int>(size, Allocator.Persistent);

        // var testJob = new TestJob()
        // {
        //     ReadHash = _readHash,
        //     WriteHash = _writeHash.AsParallelWriter()
        // };

        // var jobHandle = testJob.Schedule(size, 64);
        // jobHandle.Complete();

        // _readHash.Dispose();
        // _readHash = _writeHash;

        for (int i = 0; i < size; i++)
        {
            if (_test.ContainsKey(i))
            {
                _test[i]++;
            }
            else
            {
                _test.Add(i, 0);
            }
        }
    }

    private void OnDestroy()
    {
        _writeHash.Dispose();
    }

    private void FixedUpdate()
    {
    }
}
