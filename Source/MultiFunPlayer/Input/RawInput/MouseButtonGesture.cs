﻿using System.Windows.Input;

namespace MultiFunPlayer.Input.RawInput;

public record MouseButtonGestureDescriptor(MouseButton Button) : ISimpleInputGestureDescriptor
{
    public override string ToString() => $"[Mouse Button: {Button}]";
}

public class MouseButtonGesture : ISimpleInputGesture
{
    private readonly MouseButtonGestureDescriptor _descriptor;

    public MouseButton Button { get; }
    public IInputGestureDescriptor Descriptor => _descriptor;

    public MouseButtonGesture(MouseButtonGestureDescriptor descriptor) => _descriptor = descriptor;

    public override string ToString() => $"[Mouse Button: {Button}]";

    public static MouseButtonGesture Create(MouseButton button) => new(new(button));
}
