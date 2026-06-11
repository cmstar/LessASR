using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using WpfDataObject = System.Windows.DataObject;
using WpfIDataObject = System.Windows.IDataObject;

namespace LocalAsrClient.App.TextInjection;

internal sealed class ClipboardBackup
{
    private readonly List<ClipboardBackupItem> _items;

    private ClipboardBackup(List<ClipboardBackupItem> items)
    {
        _items = items;
    }

    public static ClipboardBackup Capture(WpfIDataObject? dataObject)
    {
        if (dataObject is null)
        {
            return new ClipboardBackup([]);
        }

        var items = new List<ClipboardBackupItem>();
        foreach (var format in dataObject.GetFormats(autoConvert: false))
        {
            try
            {
                var data = dataObject.GetData(format, autoConvert: false);
                if (CloneData(data) is { } cloned)
                {
                    items.Add(new ClipboardBackupItem(format, cloned));
                }
            }
            catch (ExternalException)
            {
            }
        }

        return new ClipboardBackup(items);
    }

    public bool IsEmpty => _items.Count == 0;

    public WpfDataObject ToDataObject()
    {
        var dataObject = new WpfDataObject();
        foreach (var item in _items)
        {
            dataObject.SetData(item.Format, item.Data);
        }

        return dataObject;
    }

    private static object? CloneData(object? data)
    {
        return data switch
        {
            null => null,
            string text => text,
            string[] texts => texts.ToArray(),
            MemoryStream stream => new MemoryStream(stream.ToArray()),
            Stream stream when stream.CanRead => CloneStream(stream),
            _ => null
        };
    }

    private static MemoryStream? CloneStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    private sealed record ClipboardBackupItem(string Format, object Data);
}
