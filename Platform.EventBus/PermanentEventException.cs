namespace Platform.EventBus;

/// <summary>
/// Постійний збій обробки: повтор нічого не змінить (порушення бізнес-правила, невалідні дані події).
/// Повідомлення одразу йде в Dead Letter Queue, без повторних спроб.
/// </summary>
public sealed class PermanentEventException(string message, Exception? inner = null) : Exception(message, inner);
