# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Windows 网络配置工具，用于查看和修改本机网络适配器的 IP 配置。

- 语言: C# (.NET Framework 4.8)
- 框架: Windows Forms
- 构建系统: MSBuild

## 构建命令

```bash
# 构建解决方案
msbuild IP_UpdateTest.sln /p:Configuration=Release

# Debug 构建
msbuild IP_UpdateTest.sln /p:Configuration=Debug
```

## 架构

```
IP_UpdateTest/
├── Program.cs              # 入口点
├── FrmMain.cs              # 主窗体 - 网络配置界面
├── FrmInfo.cs              # 报表窗体 - 网卡信息展示
├── NetworkAdapter .cs      # 网卡实体类 + WMI 配置方法
└── NetworkAdapterUtil.cs   # 网卡查询/控制工具类
```

核心依赖:
- `System.Management` (WMI): 修改网络配置
- `System.Net.NetworkInformation`: 查询网络接口

## 关键实现

网络配置通过 WMI `Win32_NetworkAdapterConfiguration` 实现:
- `NetworkAdapter.EnableDHCP()`: 启用 DHCP
- `NetworkAdapter.SetIPAddressSubMaskDnsGetway()`: 设置完整配置
- `NetworkAdapterUtil.GetEthernetWirelessNetworkAdaptersUP()`: 获取活动网卡
