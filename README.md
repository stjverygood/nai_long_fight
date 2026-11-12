# Nai Long Fight

一个使用 Godot 4 .NET / C# 制作的 3D 自动战斗观战游戏。选择 2–8 个势力后，会生成随机起伏地形；各势力采用集结冲锋、分波进攻或守点反击等随机战术，组织不同兵种自动交战。

## 运行

使用 Godot 4.7.2 .NET 版打开本目录中的 `project.godot`，运行主场景即可。

重新导出 Windows 版本：

```powershell
godot --headless --path . --export-release "Windows Desktop" build/NaiLongFight.exe
```

## 操作

- 选择势力数量后点击「开始自动战斗」
- 空格：暂停 / 继续
- 按住鼠标左键拖动：旋转视角
- WASD：移动视角
- 鼠标滚轮：缩放

士兵数达到每个势力 34 人后会停止继续增援；战斗持续到只剩一个势力存活。
