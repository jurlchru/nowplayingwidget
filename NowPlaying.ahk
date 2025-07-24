ToggleConsole(ItemName, ItemPos, MyMenu) {
  if (WinExist("ahk_id " hServer) && DllCall("IsWindowVisible", "Ptr", hServer))
  {
    WinHide("ahk_id " hServer)
  }
  else
  {
    WinShow("ahk_id " hServer)
    WinActivate("ahk_id " hServer)
  }
}

OpenWidget(ItemName, ItemPos, MyMenu) {
  Run(A_ScriptDir . "\widget.html")
}

CloseItem(ItemName, ItemPos, MyMenu) {
  DetectHiddenWindows(true)
  if (WinExist("ahk_id " hServer))
  {
    WinClose("ahk_id " hServer)
    WinWaitClose("ahk_id " hServer, , 3)
  }
  DetectHiddenWindows(false)
  ExitApp()
}

A_TrayMenu.Add("Open Widget in Browser", OpenWidget)
A_TrayMenu.Add("Show / Hide Console Window", ToggleConsole)
A_TrayMenu.Add("Exit Server", CloseItem)
A_TrayMenu.Default := "Show / Hide Console Window"
Persistent
; TraySetIcon("assets\Tray.ico")

DetectHiddenWindows(true)
Run("bin\NowPlayingMediaServer.exe", "", "Hide", &PID)
WinWait("ahk_pid " PID)
global hServer := WinExist("ahk_pid " PID)
DetectHiddenWindows(false)