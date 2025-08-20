using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq.Expressions;
using UnityEngine;

public class LinearGradient : MonoBehaviour
{
    private static readonly Dictionary<Type, Func<object, object, float, object>> LerpExecuter =
        new Dictionary<Type, Func<object, object, float, object>>
        {
            { typeof(int), (start, end, t) => (int)Mathf.Lerp((int)start, (int)end, t) },
            { typeof(float), (start, end, t) => Mathf.Lerp((float)start, (float)end, t) },
            { typeof(decimal), (start, end, t) => (decimal)Mathf.Lerp((float)(decimal)start, (float)(decimal)end, t) },
            { typeof(Vector2), (start, end, t) => Vector2.Lerp((Vector2)start, (Vector2)end, t) },
            { typeof(Vector3), (start, end, t) => Vector3.Lerp((Vector3)start, (Vector3)end, t) },
            { typeof(Vector4), (start, end, t) => Vector4.Lerp((Vector4)start, (Vector4)end, t) },
            { typeof(Color), (start, end, t) => Color.Lerp((Color)start, (Color)end, t) },
        };

    private readonly Dictionary<int, Coroutine> _runningCoroutines = new Dictionary<int, Coroutine>();
    private readonly Dictionary<int, (object component, PropertyInfo propertyInfo, object targetValue)> _lerpData = new Dictionary<int, (object component, PropertyInfo propertyInfo, object targetValue)>();
    private int _coroutineCount = 0;
    public static void CompleteAllGlobally(bool immediately = false)
    {
        LinearGradient[] allGradients = FindObjectsOfType<LinearGradient>();
        foreach (var gradient in allGradients)
        {
            gradient.CompleteAll(immediately);
        }
    }

    public void CompleteAll(bool immediately = false)
    {
        foreach (KeyValuePair<int, Coroutine> coroutine in _runningCoroutines)
        {
            if (coroutine.Value != null)
            {
                StopCoroutine(coroutine.Value);
            }
        }

        if (!immediately)
        {
            foreach (var data in _lerpData)
            {
                if (data.Value.component != null)
                    data.Value.propertyInfo.SetValue(data.Value.component, data.Value.targetValue);
            }
        }
        _lerpData.Clear();
        _runningCoroutines.Clear();
    }

    public async Task RunAsync<TComponent, TValueType>(
        string propertyName,
        TValueType targetValue,
        float durationMs = GlobalConfig.FadeInFadeOut.defaultDuration,
        float delayMs = GlobalConfig.FadeInFadeOut.defaultDelay) where TComponent : Component
    {
        Coroutine coroutine = Run<TComponent, TValueType>(propertyName, targetValue, durationMs, delayMs);

        TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
        StartCoroutine(WaitForCoroutine(coroutine, taskCompletionSource));

        // 4. 等待 TaskCompletionSource 的 Task 完成，确保协程执行完毕
        await taskCompletionSource.Task;
    }
    //  用于等待 coroutine 完成并且设置 TaskCompletionSource 的助手方法
    private IEnumerator WaitForCoroutine(Coroutine coroutine, TaskCompletionSource<bool> tcs)
    {
        yield return coroutine;
        tcs.SetResult(true);
    }

    public Coroutine Run<TComponent, TValueType>(
        string propertyName,
        TValueType targetValue,
        float durationMs = GlobalConfig.FadeInFadeOut.defaultDuration,
        float delayMs = GlobalConfig.FadeInFadeOut.defaultDelay) where TComponent : Component
    {
        TComponent component = GetComponent<TComponent>();
        if (component == null)
        {
            Debug.LogError($"Component {typeof(TComponent).Name} not found on {gameObject.name}!");
            return null;
        }

        PropertyInfo propertyInfo = typeof(TComponent).GetProperty(propertyName);
        if (propertyInfo == null)
        {
            Debug.LogError($"Property {propertyName} or type {typeof(TValueType)} is not supported!");
            return null;
        }

        object initialValue = propertyInfo.GetValue(component);
        if (!(initialValue is TValueType))
        {
            Debug.LogError($"Property {propertyName} value type mismatch! Expected {typeof(TValueType)}.");
            return null;
        }

        Coroutine coroutine = StartCoroutine(LerpCoroutine(component, propertyInfo, (TValueType)initialValue, targetValue, durationMs, delayMs, _coroutineCount));
        _runningCoroutines.Add(_coroutineCount, coroutine);
        _coroutineCount++;

        return coroutine;
    }

    private IEnumerator LerpCoroutine<TComponent, TValueType>(
        TComponent component,
        PropertyInfo propertyInfo,
        TValueType initialValue,
        TValueType targetValue,
        float durationMs,
        float delayMs,
        int indexSelf)
    {
        _lerpData.Add(indexSelf, (component, propertyInfo, targetValue));

        if (delayMs > 0)
            yield return new WaitForSeconds(delayMs / 1000f);

        float elapsedTime = 0f;
        float durationSeconds = durationMs / 1000f;

        while (elapsedTime < durationSeconds)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / durationSeconds);

            TValueType currentValue;
            if (LerpExecuter.ContainsKey(typeof(TValueType)))
                currentValue = (TValueType)LerpExecuter[typeof(TValueType)](initialValue, targetValue, t);
            else
                currentValue = GenericOperator.Lerp<TValueType>(initialValue, targetValue, t);

            propertyInfo.SetValue(component, currentValue);
            yield return null;
        }

        // 确保最终值被正确设置
        propertyInfo.SetValue(component, targetValue);

        _runningCoroutines.Remove(indexSelf);
        _lerpData.Remove(indexSelf);
    }


}

public static class GenericOperator
{
    // Cache for T op T -> T
    private static class Cache<T>
    {
        public static readonly Func<T, T, T> Add = CreateBinary((p1, p2) => Expression.Add(p1, p2), "Add");
        public static readonly Func<T, T, T> Subtract = CreateBinary((p1, p2) => Expression.Subtract(p1, p2), "Subtract");

        private static Func<T, T, T> CreateBinary(Func<Expression, Expression, BinaryExpression> bodyFunc, string opName)
        {
            try
            {
                var p1 = Expression.Parameter(typeof(T));
                var p2 = Expression.Parameter(typeof(T));
                return Expression.Lambda<Func<T, T, T>>(bodyFunc(p1, p2), p1, p2).Compile();
            }
            catch (Exception ex)
            {
                return (v1, v2) => throw new InvalidOperationException($"Type {typeof(T)} does not support {opName}.", ex);
            }
        }
    }

    // Cache for T op float -> T
    private static class Cache<T, TFactor>
    {
        public static readonly Func<T, TFactor, T> Multiply = CreateFactor((p1, p2) => Expression.Multiply(p1, p2), "Multiply");
        
        private static Func<T, TFactor, T> CreateFactor(Func<Expression, Expression, BinaryExpression> bodyFunc, string opName)
        {
             try
            {
                var p1 = Expression.Parameter(typeof(T));
                var p2 = Expression.Parameter(typeof(TFactor));
                return Expression.Lambda<Func<T, TFactor, T>>(bodyFunc(p1, p2), p1, p2).Compile();
            }
            catch (Exception ex)
            {
                return (v1, v2) => throw new InvalidOperationException($"Type {typeof(T)} does not support {opName} with factor of type {typeof(TFactor)}.", ex);
            }
        }
    }

    public static T Add<T>(T val1, T val2) => Cache<T>.Add(val1, val2);
    public static T Subtract<T>(T val1, T val2) => Cache<T>.Subtract(val1, val2);
    public static T Multiply<T>(T val, float factor) => Cache<T, float>.Multiply(val, factor);


    /// <summary>
    /// Performs a generic linear interpolation between two values of any type that supports addition, subtraction, and multiplication by a float.
    /// The calculation is: start + (end - start) * t
    /// </summary>
    /// <typeparam name="T">The type of values to interpolate.</typeparam>
    /// <param name="start">The starting value (when t=0).</param>
    /// <param name="end">The ending value (when t=1).</param>
    /// <param name="t">The interpolation factor, clamped between 0 and 1.</param>
    /// <returns>The interpolated value.</returns>
    public static T Lerp<T>(T start, T end, float t)
    {
        // Clamp t to be in the [0, 1] range.
        if (t <= 0.0f) return start;
        if (t >= 1.0f) return end;

        // Perform the calculation: start + (end - start) * t
        T difference = Subtract(end, start);
        T scaledDifference = Multiply(difference, t);
        return Add(start, scaledDifference);
    }
}