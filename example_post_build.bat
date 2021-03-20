@echo off

set BuildTargetPath=%1
set tasklist=%windir%\System32\tasklist.exe
set taskkill=%windir%\System32\taskkill.exe

goto :MAIN

:STOPPROC
    set wasStopped=0
    set procFound=0
    set notFound_result=ERROR:
    set procName=%1
    for /f "usebackq" %%A in (`%taskkill% /IM %procName%`) do (
      if NOT %%A==%notFound_result% (set procFound=1)
    )
    if %procFound%==0 (
      echo The process was not running.
      goto :EOF
    )
    set wasStopped=1
    set ignore_result=INFO:
:CHECKDEAD
	ping 127.0.0.1 -n 1 -w 3000 > NUL
    for /f "usebackq" %%A in (`%tasklist% /nh /fi "imagename eq %procName%"`) do (
      if not %%A==%ignore_result% (
		goto :CHECKDEAD
	  )
    )
	echo Process exited.
    goto :FINAL

:MAIN
echo Waiting for process to exit...
call :STOPPROC valheim_server.exe
goto :EOF

:FINAL
	copy /v %BuildTargetPath% "C:\Steam\steamapps\common\Valheim dedicated server\BepInEx\plugins\"
	start steam://rungameid/896660