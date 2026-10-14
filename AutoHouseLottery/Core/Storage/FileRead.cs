namespace AutoHouseLottery.Core.Storage;

internal readonly record struct FileRead(FileReadStatus Status, byte[]? Bytes);
