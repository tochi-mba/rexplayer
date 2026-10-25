using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Rex.Media.Interop.Shell;

/// <summary>A line of a jump list: its title and the arguments the program is started with.</summary>
public readonly record struct JumpListLine(string Title, string Arguments);

/// <summary>
/// A program's jump list through the classic custom destination list, which works without a package
/// identity. What goes on it is decided by the caller, which learns first how many lines Windows has
/// room for and which ones the user removed (those must never come back). Used on one thread.
/// </summary>
public static unsafe class JumpList
{
    private static readonly Guid ObjectArrayIid = typeof(IObjectArray).GUID;
    private static readonly Guid ShellLinkIid = typeof(IShellLinkW).GUID;

    /// <summary>
    /// Replaces the jump list of <paramref name="appId"/> (null for this program's own) with what
    /// <paramref name="plan"/> gives, from the room Windows has and the arguments of the lines the user
    /// removed: a category headed <paramref name="heading"/> and the tasks.
    /// </summary>
    public static void Publish(string? appId, string program, string heading, Func<int, IReadOnlySet<string>, (IReadOnlyList<JumpListLine> Items, IReadOnlyList<JumpListLine> Tasks)> plan)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(plan);
        var list = (ICustomDestinationList)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(DestinationList).GUID, throwOnError: true)!)!;
        try
        {
            if (appId is not null)
            {
                list.SetAppID(appId);
            }

            var iid = ObjectArrayIid;
            list.BeginList(out var slots, &iid, out var removedPointer);
            var committed = false;
            try
            {
                var removed = Arguments(removedPointer);
                var (items, tasks) = plan((int)slots, removed);
                if (items.Count > 0)
                {
                    list.AppendCategory(heading, Collection(program, items));
                }

                if (tasks.Count > 0)
                {
                    list.AddUserTasks(Collection(program, tasks));
                }

                list.CommitList();
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    list.AbortList();
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>Removes the jump list of <paramref name="appId"/> entirely.</summary>
    public static void Delete(string appId)
    {
        var list = (ICustomDestinationList)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(DestinationList).GUID, throwOnError: true)!)!;
        try
        {
            list.DeleteList(appId);
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>The arguments of each link in a removed-items array, which Windows hands over as a raw interface.</summary>
    private static HashSet<string> Arguments(void* array)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (array is null)
        {
            return found;
        }

        var objects = (IObjectArray)Marshal.GetObjectForIUnknown((nint)array);
        Marshal.Release((nint)array);
        try
        {
            objects.GetCount(out var count);
            var iid = ShellLinkIid;
            var buffer = stackalloc char[1024];
            for (uint i = 0; i < count; i++)
            {
                objects.GetAt(i, &iid, out var linkPointer);
                var link = (IShellLinkW)Marshal.GetObjectForIUnknown((nint)linkPointer);
                Marshal.Release((nint)linkPointer);
                try
                {
                    buffer[0] = '\0';
                    link.GetArguments(buffer, 1024);
                    found.Add(new string(buffer));
                }
                finally
                {
                    Marshal.ReleaseComObject(link);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(objects);
        }

        return found;
    }

    private static IObjectArray Collection(string program, IReadOnlyList<JumpListLine> lines)
    {
        var collection = (IObjectCollection)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(EnumerableObjectCollection).GUID, throwOnError: true)!)!;
        foreach (var line in lines)
        {
            collection.AddObject(Link(program, line));
        }

        return collection;
    }

    /// <summary>A shell link that starts <paramref name="program"/> with the line's arguments, titled by the line.</summary>
    private static IShellLinkW Link(string program, JumpListLine line)
    {
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID, throwOnError: true)!)!;
        link.SetPath(program);
        link.SetArguments(line.Arguments);
        link.SetIconLocation(program, 0);
        link.SetDescription(line.Title);

        var store = (IPropertyStore)link;
        var title = new PROPVARIANT();
        title.Anonymous.Anonymous.vt = Windows.Win32.System.Variant.VARENUM.VT_LPWSTR;
        title.Anonymous.Anonymous.Anonymous.pwszVal = (char*)Marshal.StringToCoTaskMemUni(line.Title);
        try
        {
            store.SetValue(PInvoke.PKEY_Title, title);
            store.Commit();
        }
        finally
        {
            PInvoke.PropVariantClear(ref title);
        }

        return link;
    }
}
