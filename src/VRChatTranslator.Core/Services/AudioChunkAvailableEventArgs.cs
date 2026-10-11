using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class AudioChunkAvailableEventArgs(AudioChunk chunk) : EventArgs
{
    public AudioChunk Chunk { get; } = chunk;
}

