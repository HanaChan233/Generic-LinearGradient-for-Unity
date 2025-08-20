[简体中文](README_zh-cn.md)

# Generic Linear Gradient Component for Unity

A flexible animation component for Unity that can smoothly interpolate any component property, as long as its type supports basic arithmetic operations. It provides a simple `Run` and `RunAsync` API to create animations for both standard Unity types and your own custom classes or structs.

## How It Works

The component uses a powerful dual-strategy approach to handle animations:

1.  **Optimized Path for Native Types**: For common Unity types, it uses a pre-configured dictionary that maps each type to Unity's highly optimized, native `Lerp` method.
2.  **Generic Fallback with Expression Trees**: If a type is not in the dictionary (like a custom struct you wrote), it dynamically generates a high-performance `Lerp` function at runtime using **Expression Trees**.

When you start an animation:
- The component uses C# **Reflection** to get the initial value of the target property.
- It starts a **Coroutine** that runs for the specified duration.
- In each frame, the coroutine calculates the new interpolated value using one of the two strategies above and updates the property.

## Supported Types

### 1. Natively Supported Types (Optimized Path)

A static dictionary provides direct access to high-performance `Lerp` functions for the following types:
- `int`
- `float`
- `decimal`
- `Vector2`
- `Vector3`
- `Vector4`
- `Color`

### 2. Custom Types (Generic Path)

What if the type isn't in the list, or it's a class/struct you wrote yourself?

No problem. As long as the type supports the following operators, the generic fallback will work automatically:
- **Addition** (`T + T`)
- **Subtraction** (`T - T`)
- **Multiplication** (`T * float`)

This allows you to animate your own data structures without writing any boilerplate code.

## How Expression Trees Make This Possible

The term "linear interpolation" (Lerp) is defined by the formula: `start + (end - start) * t`.

For a generic type `T`, you can't just write this formula in C# directly. This is where expression trees come in. They allow us to build an object model of the code at runtime, which we can then compile into an executable delegate.

Here's the process:
1.  We define parameter expressions for `start`, `end`, and `t`.
2.  We build the formula step-by-step using expression nodes like `Expression.Subtract`, `Expression.Multiply`, and `Expression.Add`.
3.  This tree of expressions is wrapped in a Lambda expression.
4.  Finally, we call the `.Compile()` method on the lambda expression. This transforms the expression tree into a highly efficient delegate (e.g., `Func<T, T, float, T>`), which can be executed just like any regular C# method.

This compilation happens automatically the first time a new type is used.

## Performance: The Smart Caching Mechanism

Compiling an expression tree is a relatively expensive operation. Doing it every frame would kill performance. To solve this, the component uses a smart, automatic caching system.

The compiled delegates for each type are stored in a **static generic nested class** (`private static class Cache<T>`).

Here's why this is so effective:
- **One-Time Compilation**: The .NET runtime ensures that the static fields of a generic type are initialized only **once** per specific type. For example, `Cache<Vector3>` and `Cache<MyCustomStruct>` are treated as two completely separate types, and each will have its initialization code run exactly once.
- **Automatic Caching**: The first time you animate a `MyCustomStruct`, the expression tree is compiled, and the resulting delegate is stored in a `static readonly` field within `Cache<MyCustomStruct>`.
- **High-Speed Access**: Every subsequent time you animate a `MyCustomStruct`, the component directly retrieves the already-compiled delegate from the cache. This is extremely fast—nearly the same performance as calling a regular, pre-compiled method.

In short, there is a small, one-time cost when a new custom type is animated for the first time. After that, all animations for that type run at maximum performance.
