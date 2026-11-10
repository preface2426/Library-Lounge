# 当前便携包启动器模板

`launcher.c`是现有极小Windows启动器源码。`LibraryLoungeLauncher.exe`从已验证R3便携包保存，SHA-256为`ad45b5e110188f7bff220c763a2cf66f06c493a429c317f111f2d021153b6797`。后续打包脚本从这里取模板，不依赖已废弃的项目根启动器。

此文件是打包资源。用户从包内完整目录启动，启动器读取同目录`Builds/active-client.txt`。本机项目使用根`Start-LibraryLounge.cmd`；不要单独运行或移动这个模板。
