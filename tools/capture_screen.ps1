param(
    [string]$ProcessName = "OpenDualHub",
    [string]$OutputPath = "C:\Users\Tyler\.gemini\antigravity\brain\fe8a3003-1f56-4f7c-9588-81d7f3881fbd\xmb_screenshot.png"
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class Win32Helper {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1

if ($proc -and $proc.MainWindowHandle -ne [IntPtr]::Zero) {
    [Win32Helper]::ShowWindowAsync($proc.MainWindowHandle, 9) # SW_RESTORE
    [Win32Helper]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 400

    $rect = New-Object Win32Helper+RECT
    [Win32Helper]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    if ($width -gt 50 -and $height -gt 50) {
        $bmp = New-Object System.Drawing.Bitmap($width, $height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
        $bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose()
        $bmp.Dispose()
        Write-Output "Window screenshot saved to $OutputPath ($width x $height)"
        exit 0
    }
}

# Fallback: Capture primary screen
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output "Full screen screenshot saved to $OutputPath ($($screen.Width) x $($screen.Height))"
