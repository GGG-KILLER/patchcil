using System.Text.RegularExpressions;

namespace PatchCil.Helpers;

internal static partial class NativeLibrary
{
    private static ReadOnlySpan<byte> ElfHeader => [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    private static ReadOnlySpan<byte> MachoMhMagic => [0xFE, 0xED, 0xFA, 0xCE];
    private static ReadOnlySpan<byte> MachoMhMagic64 => [0xFE, 0xED, 0xFA, 0xCF];
    private static ReadOnlySpan<byte> MachoFatMagic => [0xCA, 0xFE, 0xBA, 0xBE];

    public static IEnumerable<string> ListVariations(string rid, string libraryName)
    {
        // Implemented based on https://github.com/dotnet/runtime/blob/v9.0.0/src/tests/Loader/AssemblyDependencyResolver/AssemblyDependencyResolverTests/NativeDependencyTests.cs
        var isUnix = RuntimeIdentifiers.IsUnix(rid);
        var isApple = RuntimeIdentifiers.IsApple(rid);
        var isWindows = RuntimeIdentifiers.IsWindows(rid);

        // Unix-like systems prefer the version with the extension, even if one without exists.
        if (isUnix)
        {
            if (isApple)
            {
                if (!libraryName.EndsWith(".dylib", StringComparison.Ordinal))
                    yield return libraryName + ".dylib";
            }
            else
            {
                if (!libraryName.EndsWith(".so", StringComparison.Ordinal))
                    yield return libraryName + ".so";
            }
        }

        yield return libraryName;

        if (isWindows)
        {
            // Add .dll suffix if there is no executable-like suffix already.
            if (!(libraryName.EndsWith(".dll", StringComparison.Ordinal) || libraryName.EndsWith(".exe", StringComparison.Ordinal)))
                yield return libraryName + ".dll";
        }
        else if (isApple)
        {
            // Try adding lib prefix
            if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
                yield return "lib" + libraryName;

            // Unix-based systems always add the extension.
            yield return libraryName + ".dylib";

            // Unix-based systems always add the extension.
            if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
                yield return "lib" + libraryName + ".dylib";
        }
        else if (isUnix)
        {
            // Try adding lib prefix
            yield return "lib" + libraryName;

            // Unix-based systems always add the extension.
            yield return libraryName + ".so";

            // Unix-based systems always add the extension.
            if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
                yield return "lib" + libraryName + ".so";
        }
    }

    private static ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> path)
    {
        var name = Path.GetFileName(path);

        if (name.IndexOf(".so.", StringComparison.Ordinal) is > 0 and var extStart)
            return name[extStart..];
        else if (name.EndsWith(".so", StringComparison.Ordinal))
            return ".so";
        else if (name.EndsWith(".dll", StringComparison.Ordinal))
            return ".dll";
        else if (name.EndsWith(".exe", StringComparison.Ordinal))
            return ".exe";
        else if (name.EndsWith(".dylib", StringComparison.Ordinal))
            return ".dylib";
        else
            return "";
    }

    private static ReadOnlySpan<char> GetNameWithoutExtension(ReadOnlySpan<char> libraryName)
    {
        var name = Path.GetFileName(libraryName);

        if (name.IndexOf(".so.", StringComparison.Ordinal) is > 0 and var extStart)
            return name[..extStart];
        else if (name.EndsWith(".so", StringComparison.Ordinal))
            return name[..^".so".Length];
        else if (name.EndsWith(".dll", StringComparison.Ordinal))
            return name[..^".dll".Length];
        else if (name.EndsWith(".exe", StringComparison.Ordinal))
            return name[..^".exe".Length];
        else if (name.EndsWith(".dylib", StringComparison.Ordinal))
            return name[..^".dylib".Length];
        else
            return name;
    }

    public static bool IsForAnotherRuntime(string rid, string libraryName)
    {
        var extension = GetExtension(libraryName);

        // We cannot reliably determine whether a library is meant for another
        // runtime if it has no extension.
        if (extension.Length < 1)
            return false;

        if (RuntimeIdentifiers.IsApple(rid))
        {
            return !extension.Equals(".dylib", StringComparison.Ordinal);
        }
        else if (RuntimeIdentifiers.IsUnix(rid))
        {
            return !extension.StartsWith(".so", StringComparison.Ordinal);
        }
        else
        {
            return !extension.Equals(".dll", StringComparison.Ordinal)
                && !extension.Equals(".exe", StringComparison.Ordinal);
        }
    }

    private static bool IsElf(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) >= header.Length
               && header.SequenceEqual(ElfHeader);
    }

    private static bool IsMacho(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return false;

        // MH_MAGIC || MH_MAGIC_64 || FAT_MAGIC
        if (header.SequenceEqual(MachoMhMagic) || header.SequenceEqual(MachoMhMagic64) || header.SequenceEqual(MachoFatMagic))
            return true;

        // MH_CIGAM || MH_CIGAM_64 || FAT_CIGAM
        header.Reverse();
        return header.SequenceEqual(MachoMhMagic) || header.SequenceEqual(MachoMhMagic64) || header.SequenceEqual(MachoFatMagic);
    }

    public static bool IsLibrary(string rid, string path)
    {
        if (RuntimeIdentifiers.IsApple(rid))
        {
            return IsMacho(path);
        }
        else if (RuntimeIdentifiers.IsUnix(rid))
        {
            return IsElf(path);
        }
        else
        {
            // TODO: Implement proper PE verification.
            return Path.GetExtension(path) is ".dll" or ".exe";
        }
    }

    // TODO: Expand with more?
    // TODO: How to determine what should and shouldn't be here?
    // NOTE: Current list was built by running `nix-locate --at-root --regex '/lib/[^/]+\.so'` and
    //       painstakingly going through the results and selecting only the libraries that *seemed*
    //       popular enough and couldn't be confused with another platform's or a generic library
    //       name that another program for another platform could use accidentally.
    // INFO: The regex source generator can generate a better matcher code than I can, so use that instead.
    [GeneratedRegex(
        // ReSharper disable once StringLiteralTypo
        @"^(?:lib)?(?>apparmor|asound|atopology|bpf|c|cgroup|cups|cupsimage|dl|gdk-3|gio-2\.0|girepository-2\.0|glib-2\.0|glib-2\.0|GLU|gmodule-2\.0|gmpris|gnome-keyring|gnomekbd|gnomekbdui|gobject-2\.0|gobject-2\.0|gthread-2\.0|gtk-3|gudev-1\.0|ICE|iconv|ip4tc|ip6tc|ipq|jack|jacknet|jackserver|ncurses|ncursesw|nftables|pipewire-0\.3|Plasma|PlasmaQuick|polkit-agent-1|polkit-gobject-1|pulse|pulse-mainloop-glib|pulse-simple|seccomp|selinux|SM|systemd|udev|uring|uring-ffi|va|va-drm|va-egl|va-glx|va-tpi|va-wayland|va-x11|vdpau|virt|virt-admin|virt-lxc|virt-qemu|wayland-client|wayland-cursor|wayland-egl|wayland-server|X11|X11-xcb|Xau|Xau7|Xaw|Xaw6|xcb|xcb-composite|xcb-damage|xcb-dbe|xcb-dpms|xcb-dri2|xcb-dri3|xcb-glx|xcb-present|xcb-randr|xcb-record|xcb-render|xcb-res|xcb-screensaver|xcb-shape|xcb-shm|xcb-sync|xcb-xf86dri|xcb-xfixes|xcb-xinerama|xcb-xinput|xcb-xkb|xcb-xtest|xcb-xv|xcb-xvmc|Xcomp|Xcomposite|Xcursor|xcvt|Xdamage|Xdmcp|Xext|Xfixes|Xfont|Xft|Xi|Xinerama|Xmu|Xmuu|Xp|Xpm|Xpresent|Xrandr|Xrender|XRes|xshmfence|Xss|Xt|xtables|XTrap|Xtst|Xv|XvMC|XvMCW)$",
        RegexOptions.CultureInvariant,
        "en-US")]
    private static partial Regex IsKnownUnixLibraryRegex { get; }

    public static bool IsKnownUnixLibraryName(string libraryName) =>
        IsKnownUnixLibraryRegex.IsMatch(GetNameWithoutExtension(libraryName));

    // List obtained from https://pinvoke.net
    // INFO: The regex source generator can generate a better matcher code than I can, so use that instead.
    [GeneratedRegex(
        // ReSharper disable once StringLiteralTypo
        @"^(?>advapi32|avifil32|cards|cfgmgr32|comctl32|comdlg32|credui|crypt32|dbghelp|dbghlp|dbghlp32|dhcpsapi|difxapi|dmcl40|dnsapi|dtl|dwmapi|faultrep|fbwflib|fltlib|fwpuclnt|gdi32|gdiplus|getuname|glu32|glut32|gsapi|hhctrl|hid|hlink|httpapi|icmp|imm32|iphlpapi|iprop|irprops|kernel32|mapi32|MinCore|mpr|mqrt|mscorsn|msdelta|msdrm|msi|msports|msvcrt|ncrypt|netapi32|ntdll|ntdsapi|odbc32|odbccp32|ole32|oleacc|oleaut32|opengl32|pdh|powrprof|printui|propsys|psapi|pstorec|query|quickusb|rasapi32|rpcrt4|scarddlg|secur32|setupapi|shell32|shlwapi|twain_32|unicows|urlmon|user32|userenv|uxtheme|version|wer|wevtapi|winfax|winhttp|wininet|winmm|winscard|winspool|wintrust|winusb|wlanapi|ws2_32|wtsapi32|xolehlp|xpsprint)$",
        RegexOptions.CultureInvariant,
        "en-US")]
    private static partial Regex IsKnownWindowsLibraryNameRegex { get; }

    public static bool IsKnownWindowsLibraryName(string libraryName) =>
        IsKnownWindowsLibraryNameRegex.IsMatch(GetNameWithoutExtension(libraryName));
}
