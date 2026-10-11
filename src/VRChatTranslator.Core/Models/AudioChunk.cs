namespace VRChatTranslator.Core.Models;

public sealed record AudioChunk(
    DateTimeOffset CapturedAt,
    ReadOnlyMemory<byte> Data,
    AudioFormatInfo Format,
    long DevicePosition,
    long QpcPosition);

