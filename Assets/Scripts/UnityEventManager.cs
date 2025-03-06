using System.Collections.Generic;
using UnityEngine.Events;

public class UnityEventManager
{
    public List<UnityEventHandler> EventHandlers = new();

    public void RegisterEvent(UnityEvent unityEvent, UnityAction unityAction)
    {
        var eventHandler = new UnityEventHandlerNoArg(unityEvent, unityAction);
        EventHandlers.Add(eventHandler);
    }

    public void RegisterEvents(params (UnityEvent, UnityAction)[] events)
    {
        foreach (var item in events)
            RegisterEvent(item.Item1, item.Item2);
    }

    public void RegisterEvent<T>(UnityEvent<T> unityEvent, UnityAction<T> unityAction)
    {
        var eventHandler = new UnityEventHandler<T>(unityEvent, unityAction);
        EventHandlers.Add(eventHandler);
    }

    public void RegisterEvents<T>(params (UnityEvent<T>, UnityAction<T>)[] events)
    {
        foreach (var item in events)
            RegisterEvent(item.Item1, item.Item2);
    }

    public void RegisterEvent<T0, T1>(UnityEvent<T0, T1> unityEvent, UnityAction<T0, T1> unityAction)
    {
        var eventHandler = new UnityEventHandler<T0, T1>(unityEvent, unityAction);
        EventHandlers.Add(eventHandler);
    }

    public void RegisterEvents<T0, T1>(params (UnityEvent<T0, T1>, UnityAction<T0, T1>)[] events)
    {
        foreach (var item in events)
            RegisterEvent(item.Item1, item.Item2);
    }

    public void RegisterEvent<T0, T1, T2>(UnityEvent<T0, T1, T2> unityEvent, UnityAction<T0, T1, T2> unityAction)
    {
        var eventHandler = new UnityEventHandler<T0, T1, T2>(unityEvent, unityAction);
        EventHandlers.Add(eventHandler);
    }

    public void RegisterEvents<T0, T1, T2>(params (UnityEvent<T0, T1, T2>, UnityAction<T0, T1, T2>)[] events)
    {
        foreach (var item in events)
            RegisterEvent(item.Item1, item.Item2);
    }

    public void RegisterEvent<T0, T1, T2, T3>(UnityEvent<T0, T1, T2, T3> unityEvent, UnityAction<T0, T1, T2, T3> unityAction)
    {
        var eventHandler = new UnityEventHandler<T0, T1, T2, T3>(unityEvent, unityAction);
        EventHandlers.Add(eventHandler);
    }

    public void RegisterEvents<T0, T1, T2, T3>(params (UnityEvent<T0, T1, T2, T3>, UnityAction<T0, T1, T2, T3>)[] events)
    {
        foreach (var item in events)
            RegisterEvent(item.Item1, item.Item2);
    }

    public void AddListeners()
    {
        UpdateEvents(ListenerAction.Add);
    }

    public void RemoveListeners() 
    {
        UpdateEvents(ListenerAction.Remove);
    }

    public void UpdateEvents(ListenerAction action)
    {
        EventHandlers.ForEach(x =>
        {
            switch (action)
            {
                case ListenerAction.Add:
                    x.Add();
                    return;
                case ListenerAction.Remove:
                    x.Remove();
                    return;
            }
        });
    }
}

public enum ListenerAction
{
    Add,
    Remove,
}

public abstract class UnityEventHandler
{
    public abstract void Add();
    public abstract void Remove();
}

public class UnityEventHandlerNoArg : UnityEventHandler
{
    private UnityEvent _unityEvent;
    private UnityAction _unityAction;

    public UnityEventHandlerNoArg(UnityEvent unityEvent, UnityAction unityAction) 
    {
        _unityEvent = unityEvent;
        _unityAction = unityAction;
    }

    public override void Add()
    {
        _unityEvent.AddListener(_unityAction);
    }

    public override void Remove()
    {
        _unityEvent.RemoveListener(_unityAction);
    }
}

public class UnityEventHandler<T> : UnityEventHandler
{
    public UnityEventHandler(UnityEvent<T> unityEvent, UnityAction<T> unityAction)
    {
        _unityEvent = unityEvent;
        _unityAction = unityAction;
    }

    private UnityEvent<T> _unityEvent;
    private UnityAction<T> _unityAction;

    public override void Add()
    {
        _unityEvent.AddListener(_unityAction);
    }

    public override void Remove()
    {
        _unityEvent.RemoveListener(_unityAction);
    }
}

public class UnityEventHandler<T0, T1> : UnityEventHandler
{
    public UnityEventHandler(UnityEvent<T0, T1> unityEvent, UnityAction<T0, T1> unityAction)
    {
        _unityEvent = unityEvent;
        _unityAction = unityAction;
    }

    private UnityEvent<T0, T1> _unityEvent;
    private UnityAction<T0, T1> _unityAction;

    public override void Add()
    {
        _unityEvent.AddListener(_unityAction);
    }

    public override void Remove()
    {
        _unityEvent.RemoveListener(_unityAction);
    }
}

public class UnityEventHandler<T0, T1, T2> : UnityEventHandler
{
    public UnityEventHandler(UnityEvent<T0, T1, T2> unityEvent, UnityAction<T0, T1, T2> unityAction)
    {
        _unityEvent = unityEvent;
        _unityAction = unityAction;
    }

    private UnityEvent<T0, T1, T2> _unityEvent;
    private UnityAction<T0, T1, T2> _unityAction;

    public override void Add()
    {
        _unityEvent.AddListener(_unityAction);
    }

    public override void Remove()
    {
        _unityEvent.RemoveListener(_unityAction);
    }
}

public class UnityEventHandler<T0, T1, T2, T3> : UnityEventHandler
{
    public UnityEventHandler(UnityEvent<T0, T1, T2, T3> unityEvent, UnityAction<T0, T1, T2, T3> unityAction)
    {
        _unityEvent = unityEvent;
        _unityAction = unityAction;
    }

    private UnityEvent<T0, T1, T2, T3> _unityEvent;
    private UnityAction<T0, T1, T2, T3> _unityAction;

    public override void Add()
    {
        _unityEvent.AddListener(_unityAction);
    }

    public override void Remove()
    {
        _unityEvent.RemoveListener(_unityAction);
    }
}