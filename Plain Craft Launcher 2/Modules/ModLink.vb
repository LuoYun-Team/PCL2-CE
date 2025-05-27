﻿Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports Open.Nat
Imports System.Net
Imports System.Net.Sockets
Imports Makaretu.Nat
Imports STUN
Imports System.Net.NetworkInformation
Imports PCL.PageLinkNetStatus

Public Class ModLink

#Region "MCPing"
    Public Class WorldInfo
        Public Property Port As Integer
        Public Property VersionName As String
        Public Property PlayerMax As Integer
        Public Property PlayerOnline As Integer
        Public Property Description As String
        Public Property Favicon As String

        Public Overrides Function ToString() As String
            Return $"[MCPing] Version: {VersionName}, Players: {PlayerOnline}/{PlayerMax}, Description: {Description}"
        End Function
    End Class

    Public Class MCPing


        Sub New(IP As String, Optional Port As Integer = 25565)
            _IP = IP
            _Port = Port
        End Sub

        Private _IP As String
        Private _Port As Integer

        ''' <summary>
        ''' 对疑似 MC 端口进行 MCPing，并返回相关信息
        ''' </summary>
        Public Async Function GetInfo() As Tasks.Task(Of WorldInfo)
            Try
                ' 创建 TCP 客户端并连接到服务器
                Using client As New TcpClient(_IP, _Port)
                    Log($"[MCPing] Established connection ({_IP}:{_Port})", LogLevel.Debug)
                    ' 向服务器发送握手数据包
                    Using stream = client.GetStream()
                        If Not stream.CanWrite OrElse Not stream.CanRead Then Return Nothing

                        Dim handshake As Byte() = BuildHandshake(_IP, _Port)
                        Log($"[MCPing] Sending {String.Join(" ", handshake)}", LogLevel.Debug)
                        Await stream.WriteAsync(handshake, 0, handshake.Length)
                        Log($"[MCPing] Sended handshake", LogLevel.Debug)

                        ' 向服务器发送查询状态信息的数据包
                        Dim statusRequest As Byte() = BuildStatusRequest()
                        Log($"[MCPing] Sending {String.Join(" ", statusRequest)}")
                        Await stream.WriteAsync(statusRequest, 0, statusRequest.Length)
                        Log($"[MCPing] Sended statusrequest", LogLevel.Debug)

                        ' 读取服务器响应的数据
                        Dim res As New List(Of Byte)
                        Dim buffer(4096) As Byte

                        ' 读取varInt头部
                        Dim packetLength As Integer = 0
                        Dim bytesNeeded = 5
                        Do
                            Dim bytesRead = Await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, bytesNeeded))
                            If bytesRead = 0 Then Exit Do

                            Dim parseResult = ParseVarInt(buffer.Take(bytesRead).ToArray(), packetLength)
                            If parseResult.Success Then
                                bytesNeeded = parseResult.BytesNeeded
                                If bytesNeeded = 0 Then Exit Do
                            Else
                                Exit Do
                            End If
                        Loop While bytesNeeded > 0
                        packetLength -= 3
                        Log($"[MCPing] Got packet length ({packetLength})", LogLevel.Debug)

                        ' 读取剩余数据包
                        Dim totalBytes = 0
                        Do
                            Dim bytesRead = Await stream.ReadAsync(buffer, 0, buffer.Length)
                            If bytesRead = 0 Then Exit Do
                            res.AddRange(buffer.Take(bytesRead))
                            totalBytes += bytesRead
                            Log($"[MCPing] Received part ({bytesRead})", LogLevel.Debug)
                        Loop While totalBytes < packetLength

                        Log($"[MCPing] Received ({res.Count})", LogLevel.Debug)

                        Dim response As String = Encoding.UTF8.GetString(res.ToArray(), 0, res.Count)
                        Dim startIndex = response.IndexOf("{""")
                        If startIndex > 10 Then Return Nothing
                        response = response.Substring(startIndex)
                        Log("[MCPing] Server Response: " & response, LogLevel.Debug)

                        Dim j = JObject.Parse(response)

                        Dim world As New WorldInfo With {
                        .VersionName = j("version")("name"),
                        .PlayerMax = j("players")("max"),
                        .PlayerOnline = j("players")("online"),
                        .Favicon = If(j("favicon"), ""),
                        .Port = _Port
                        }
                        Dim descObj = j("description")
                        world.Description = ""
                        If descObj.Type = JTokenType.Object AndAlso descObj("extra") IsNot Nothing Then
                            Log("[MCPing] 获取到的内容为 extra 形式", LogLevel.Debug)
                            world.Description = MinecraftFormatter.ConvertToMinecraftFormat(descObj)
                        ElseIf descObj.Type = JTokenType.Object AndAlso descObj("text") IsNot Nothing Then
                            Log("[MCPing] 获取到的内容为 text 形式", LogLevel.Debug)
                            world.Description = descObj("text").ToString()
                        ElseIf descObj.Type = JTokenType.String Then
                            Log("[MCPing] 获取到的内容为 string 形式", LogLevel.Debug)
                            world.Description = descObj.ToString()
                        End If
                        Return world
                    End Using
                End Using
            Catch ex As Exception
                Log(ex, "[MCPing] Error: " & ex.Message)
            End Try
            Return Nothing
        End Function


        Function BuildHandshake(serverIp As String, serverPort As Integer) As Byte()
            ' 构建握手数据包
            Dim handshake As New List(Of Byte)
            handshake.AddRange(GetVarInt(0)) ' 数据包 ID 握手包
            handshake.AddRange(GetVarInt(578)) ' 协议
            Dim encodedIP = Encoding.UTF8.GetBytes(serverIp)
            handshake.AddRange(GetVarInt(encodedIP.Length)) ' 服务器地址长度
            handshake.AddRange(encodedIP) ' 服务器地址
            handshake.AddRange(BitConverter.GetBytes(CUShort(serverPort)).Reverse()) ' 服务器端口
            handshake.AddRange(GetVarInt(1)) ' 下一个状态 获取服务器状态

            handshake.InsertRange(0, GetVarInt(handshake.Count))

            Return handshake.ToArray()
        End Function

        Function BuildStatusRequest() As Byte()
            ' 构建状态请求数据包
            Dim packet As New List(Of Byte)
            packet.AddRange(GetVarInt(1))
            packet.AddRange(GetVarInt(0))
            Return packet.ToArray() ' 状态请求数据包
        End Function

        Private Function ParseVarInt(bytes As Byte(), ByRef value As Integer) As (Success As Boolean, BytesNeeded As Integer, Value As Integer)
            value = 0
            Dim shift = 0
            Dim index = 0

            Do While index < bytes.Length
                Dim b = bytes(index)
                value = value Or (CInt(b And &H7F) << shift)
                shift += 7
                index += 1

                If (b And &H80) = 0 Then
                    Return (True, 0, value)
                End If

                If index >= 5 Then
                    Return (False, 0, 0)
                End If
            Loop

            Return (False, 5 - index, 0)
        End Function

        Private Function GetVarInt(value As Integer) As Byte()
            If value < 0 Then Return {}
            Dim result As New List(Of Byte)
            Do
                Dim temp As Byte = CByte(value And &H7F)
                value >>= 7
                If value <> 0 Then
                    temp = temp Or &H80
                End If
                result.Add(temp)
            Loop While value <> 0
            Return result.ToArray()
        End Function
    End Class
#End Region

#Region "端口查找"
    Public Class PortFinder
        ' 定义需要的结构和常量
        <StructLayout(LayoutKind.Sequential)>
        Public Structure MIB_TCPROW_OWNER_PID
            Public dwState As Integer
            Public dwLocalAddr As Integer
            Public dwLocalPort As Integer
            Public dwRemoteAddr As Integer
            Public dwRemotePort As Integer
            Public dwOwningPid As Integer
        End Structure

        <DllImport("iphlpapi.dll", SetLastError:=True)>
        Public Shared Function GetExtendedTcpTable(
        ByVal pTcpTable As IntPtr,
        ByRef dwOutBufLen As Integer,
        ByVal bOrder As Boolean,
        ByVal ulAf As Integer,
        ByVal TableClass As Integer,
        ByVal reserved As Integer) As Integer
        End Function

        Public Shared Function GetProcessPort(ByVal dwProcessId As Integer) As List(Of Integer)
            Dim ports As New List(Of Integer)
            Dim tcpTable As IntPtr = IntPtr.Zero
            Dim dwSize As Integer = 0
            Dim dwRetVal As Integer

            If dwProcessId = 0 Then
                Return ports
            End If

            dwRetVal = GetExtendedTcpTable(IntPtr.Zero, dwSize, True, 2, 5, 0)
            If dwRetVal <> 0 AndAlso dwRetVal <> 122 Then ' 122 表示缓冲区不足
                Return ports
            End If

            tcpTable = Marshal.AllocHGlobal(dwSize)
            Try
                If GetExtendedTcpTable(tcpTable, dwSize, True, 2, 5, 0) <> 0 Then
                    Return ports
                End If

                Dim tablePtr As IntPtr = tcpTable
                Dim dwNumEntries As Integer = Marshal.ReadInt32(tablePtr)
                tablePtr = IntPtr.Add(tablePtr, 4)

                For i As Integer = 0 To dwNumEntries - 1
                    Dim row As MIB_TCPROW_OWNER_PID = Marshal.PtrToStructure(Of MIB_TCPROW_OWNER_PID)(tablePtr)
                    If row.dwOwningPid = dwProcessId Then
                        ports.Add(row.dwLocalPort >> 8 Or (row.dwLocalPort And &HFF) << 8) ' 转换端口号
                    End If
                    tablePtr = IntPtr.Add(tablePtr, Marshal.SizeOf(Of MIB_TCPROW_OWNER_PID)())
                Next
            Finally
                Marshal.FreeHGlobal(tcpTable)
            End Try

            Return ports
        End Function
    End Class
#End Region

#Region "UPnP 映射"

    Public Enum UPnPStatusType
        Disabled
        Enabled
        Unsupported
        Failed
    End Enum
    ''' <summary>
    ''' UPnP 状态，可能值："Disabled", "Enabled", "Unsupported", "Failed"
    ''' </summary>
    Public Shared UPnPStatus As UPnPStatusType = Nothing
    Public Shared UPnPMappingName As String = "PCL2 CE Link Lobby"
    Public Shared UPnPDevice = Nothing
    Public Shared CurrentUPnPMapping As Mapping = Nothing
    Public Shared UPnPPublicPort As String = Nothing

    ''' <summary>
    ''' 寻找 UPnP 设备并尝试创建一个 UPnP 映射
    ''' </summary>
    Public Shared Async Sub CreateUPnPMapping(Optional LocalPort As Integer = 25565, Optional PublicPort As Integer = 10240)
        Log($"[UPnP] 尝试创建 UPnP 映射，本地端口：{LocalPort}，远程端口：{PublicPort}，映射名称：{UPnPMappingName}")

        UPnPPublicPort = PublicPort
        Dim UPnPDiscoverer = New NatDiscoverer()
        Dim cts = New CancellationTokenSource(10000)
        Try
            UPnPDevice = Await UPnPDiscoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts)

            CurrentUPnPMapping = New Mapping(Protocol.Tcp, LocalPort, PublicPort, UPnPMappingName)
            Await UPnPDevice.CreatePortMapAsync(CurrentUPnPMapping)

            Await UPnPDevice.CreatePortMapAsync(New Mapping(Protocol.Tcp, LocalPort, PublicPort, "PCL2 Link Lobby"))

            UPnPStatus = UPnPStatusType.Enabled
            Hint("UPnP 映射已创建")
        Catch NotFoundEx As NatDeviceNotFoundException
            UPnPStatus = UPnPStatusType.Unsupported
            CurrentUPnPMapping = Nothing
            Log("[UPnP] 找不到可用的 UPnP 设备")
        Catch ex As Exception
            UPnPStatus = UPnPStatusType.Failed
            CurrentUPnPMapping = Nothing
            Log("[UPnP] UPnP 映射创建失败: " + ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' 尝试移除现有 UPnP 映射记录
    ''' </summary>
    Public Shared Async Sub RemoveUPnPMapping()
        Log($"[UPnP] 尝试移除 UPnP 映射，本地端口：{CurrentUPnPMapping.PrivatePort}，远程端口：{CurrentUPnPMapping.PublicPort}，映射名称：{UPnPMappingName}")

        Try
            Await UPnPDevice.DeletePortMapAsync(CurrentUPnPMapping)

            UPnPStatus = UPnPStatusType.Disabled
            CurrentUPnPMapping = Nothing
            Log("[UPnP] UPnP 映射移除成功")
        Catch ex As Exception
            UPnPStatus = UPnPStatusType.Failed
            CurrentUPnPMapping = Nothing
            Log("[UPnP] UPnP 映射移除失败: " + ex.ToString())
        End Try
    End Sub

#End Region

#Region "Minecraft 实例探测"
    Public Shared Async Function MCInstanceFinding() As Tasks.Task(Of List(Of WorldInfo))
        'Java 进程 PID 查询
        Dim PIDLookupResult As New List(Of String)
        Dim JavaNames As New List(Of String)
        JavaNames.Add("java")
        JavaNames.Add("javaw")

        For Each java In JavaNames
            Dim JavaProcesses As Process() = Process.GetProcessesByName(java)
            Log($"[MCDetect] 找到 {java} 进程 {JavaProcesses.Length} 个")

            If JavaProcesses Is Nothing OrElse JavaProcesses.Length = 0 Then
                Continue For
            Else
                For Each p In JavaProcesses
                    Log("[MCDetect] 检测到 Java 进程，PID: " + p.Id.ToString())
                    PIDLookupResult.Add(p.Id.ToString())
                Next
            End If
        Next

        Dim res As New List(Of WorldInfo)
        Try
            If Not PIDLookupResult.Any Then Return res
            Dim ports = PortFinder.GetProcessPort(Integer.Parse(PIDLookupResult.First))
            Log($"[MCDetect] 获取到端口数量 {ports.Count}")
            For Each port In ports
                Log($"[MCDetect] 找到疑似端口，开始验证：{port}")
                Dim test As New MCPing("127.0.0.1", port)
                Dim info = Await test.GetInfo()
                If Not String.IsNullOrWhiteSpace(info.VersionName) Then
                    Log($"[MCDetect] 端口 {port} 为有效 Minecraft 世界")
                    res.Add(info)
                End If
            Next
        Catch ex As Exception
            Log(ex, "[MCDetect] 获取端口信息错误", LogLevel.Debug)
        End Try
        Return res
    End Function
#End Region

#Region "NAT 穿透"
    Public Shared NATEndpoints As List(Of LeasedEndpoint) = Nothing
    ''' <summary>
    ''' 尝试进行 NAT 映射
    ''' </summary>
    ''' <param name="localPort">本地端口</param>
    Public Shared Async Sub CreateNATTranversal(LocalPort As String)
        Log($"开始尝试进行 NAT 穿透，本地端口 {LocalPort}")
        Try
            NATEndpoints = New List(Of LeasedEndpoint) '寻找 NAT 设备
            For Each nat In NatDiscovery.GetNats()
                Dim lease = Await nat.CreatePublicEndpointAsync(ProtocolType.Tcp, LocalPort)
                Dim endpoint = New LeasedEndpoint(lease)
                NATEndpoints.Add(endpoint)
                PageLinkLobby.PublicIPPort = endpoint.ToString()
                Log($"NAT 穿透完成，公网地址: {endpoint}")
            Next
        Catch ex As Exception
            Log("尝试进行 NAT 穿透失败: " + ex.ToString())
        End Try

    End Sub

    ''' <summary>
    ''' 移除 NAT 映射
    ''' </summary>
    Public Shared Sub RemoveNATTranversal()
        Log("开始尝试移除 NAT 映射")
        Try
            For Each endpoint In NATEndpoints
                endpoint.Dispose()
            Next
            Log("NAT 映射已移除")
        Catch ex As Exception
            Log("尝试移除 NAT 映射失败: " + ex.ToString())
        End Try
    End Sub
#End Region

#Region "EasyTier"

    Public Shared ETProcess As New Process
    Public Shared ETNetworkName As String = "PCLCELobby"
    Public Shared ETNetworkSecret As String = "PCLCELobbyDefault"
    Public Shared ETServer As String = Nothing '"tcp://public.easytier.cn:11010"
    Public Shared ETPath As String = PathTemp + "EasyTier\easytier-windows-x86_64"
    Public Shared IsETRunning As Boolean = False

    Public Shared Sub LaunchEasyTier(IsHost As Boolean, Optional Name As String = "PCLCELobby", Optional Secret As String = "PCLCELobbyDefault")
        Try
            ETProcess = New Process
            ETProcess.StartInfo = New ProcessStartInfo With {
                .FileName = $"{ETPath}\easytier-core.exe",
                .WorkingDirectory = ETPath,
                .Arguments = ETProcess.StartInfo.Arguments,
                .ErrorDialog = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden,
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .RedirectStandardInput = True}
            ETProcess.EnableRaisingEvents = True
            If Not File.Exists(ETProcess.StartInfo.FileName) Then
                Log("[Link] EasyTier 不存在，开始下载")
                DownloadEasyTier(True, IsHost, Name, Secret)
            End If
            Log($"[Link] EasyTier 路径: {ETProcess.StartInfo.FileName}")

            If IsHost Then
                ETNetworkName = "PCLCELobby"
                For index = 1 To 8 '生成 8 位随机编号
                    ETNetworkName += RandomInteger(0, 9).ToString()
                Next
                Log($"[Link] 本机作为创建者创建大厅，EasyTier 网络名称: {ETNetworkName}, 是否自定义网络密钥: {Not Secret = "PCLCELobbyDefault"}")
                ETProcess.StartInfo.Arguments = $"-i 10.114.51.41 --network-name {ETNetworkName} --network-secret {ETNetworkSecret} -p {ETServer} --no-tun" '创建者
            Else
                ETNetworkName = "PCLCELobby" + Name
                Log($"[Link] 本机作为加入者加入大厅，EasyTier 网络名称: {ETNetworkName}")
                ETProcess.StartInfo.Arguments = $"-d --network-name {ETNetworkName} --network-secret {ETNetworkSecret} -p {ETServer}" '加入者
                ETProcess.StartInfo.Verb = "runas"
            End If

            '创建防火墙规则
            'Dim FirewallProcess As New Process With {
            '    .StartInfo = New ProcessStartInfo With {
            '        .Verb = "runas",
            '        .FileName = "cmd",
            '        .Arguments = $"/c netsh advfirewall firewall add rule name=""PCLCE Lobby - EasyTier"" dir=in action=allow program=""{ETPath}\easytier-core.exe"" protocol=tcp localport={FrmLinkLobby.LocalPort}"
            '    }
            '}

            ETProcess.StartInfo.Arguments += $" --enable-kcp-proxy --latency-first --use-smoltcp"
            'AddHandler ETProcess.Exited, AddressOf LaunchEasyTier
            Log($"[Link] 启动 EasyTier")
            'Log($"[Link] 启动 EasyTier, 参数: {ETProcess.StartInfo.Arguments}")
            RunInUi(Sub() FrmLinkLobby.LabFinishId.Text = ETNetworkName.Replace("PCLCELobby", ""))
            ETProcess.Start()
            IsETRunning = True
            Thread.Sleep(2000)
            'Log(ETProcess.StandardOutput.ReadToEnd())
            'Log(ETProcess.StandardError.ReadToEnd())
            'If ETProcess.ExitCode = 0 Then
            '    Log("[Link] EasyTier 进程已结束，正常退出")
            'End If

        Catch ex As Exception
            Log("[Link] 尝试启动 EasyTier 时遇到问题: " + ex.ToString())
            ETProcess = Nothing
        End Try
    End Sub

    Public Shared Sub DownloadEasyTier(Optional LaunchAfterDownload As Boolean = False, Optional IsHost As Boolean = False, Optional Name As String = "PCLCELobby", Optional Secret As String = "PCLCELobbyDefault")
        Dim DlTargetPath As String = PathTemp + "EasyTier\EasyTier.zip"
        RunInNewThread(Sub()
                           Try
                               '构造步骤加载器
                               Dim Loaders As New List(Of LoaderBase)
                               '下载
                               Dim Address As New List(Of String)
                               Address.Add("https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v2.2.2/easytier-windows-x86_64-v2.2.2.zip")
                               Address.Add("https://github.com/EasyTier/EasyTier/releases/download/v2.2.2/easytier-windows-x86_64-v2.2.2.zip")

                               Loaders.Add(New LoaderDownload("下载 EasyTier", New List(Of NetFile) From {New NetFile(Address.ToArray, DlTargetPath, New FileChecker(MinSize:=1024 * 64))}) With {.ProgressWeight = 15})
                               Loaders.Add(New LoaderTask(Of Integer, Integer)("解压文件", Sub() ExtractFile(DlTargetPath, PathTemp + "EasyTier")))
                               Loaders.Add(New LoaderTask(Of Integer, Integer)("清理文件", Sub() File.Delete(DlTargetPath)))
                               If LaunchAfterDownload Then
                                   Loaders.Add(New LoaderTask(Of Integer, Integer)("启动 EasyTier", Sub() LaunchEasyTier(IsHost, Name, Secret)))
                               End If
                               '启动
                               Dim Loader As New LoaderCombo(Of JObject)("EasyTier 下载", Loaders)
                               Loader.Start()
                               'LoaderTaskbarAdd(Loader)
                               'FrmMain.BtnExtraDownload.ShowRefresh()
                               'FrmMain.BtnExtraDownload.Ribble()
                           Catch ex As Exception
                               Log(ex, "[Link] 下载 EasyTier 依赖文件失败", LogLevel.Hint)
                               Hint("下载 EasyTier 依赖文件失败，请检查网络连接", HintType.Critical)
                           End Try
                       End Sub)
    End Sub

    Public Shared Sub ExitEasyTier()
        Try
            Log("[Link] 停止 EasyTier")
            ETProcess.Kill()
            IsETRunning = False
            ETProcess = Nothing
        Catch ex As Exception
            Log("[Link] 尝试停止 EasyTier 进程时遇到问题: " + ex.ToString())
            ETProcess = Nothing
        End Try
    End Sub

#End Region

#Region "NAT 测试"
    ''' <summary>
    ''' 进行网络测试，包括 IPv4 NAT 类型测试和 IPv6 支持情况测试
    ''' </summary>
    ''' <returns>NAT 类型 + IPv6 支持与否</returns>
    Public Shared Function NetTest() As String()
        '申请通过防火墙以准确测试 NAT 类型
        Dim RetryTime As Integer = 0
        Try
PortRetry:
            Dim TestTcpListener = TcpListener.Create(RandomInteger(20000, 65000))
            TestTcpListener.Start()
            Thread.Sleep(200)
            TestTcpListener.Stop()
        Catch ex As Exception
            Log(ex, "[Link] 请求防火墙通过失败")
            If RetryTime >= 3 Then
                Log("[Link] 请求防火墙通过失败次数已达 3 次，不再重试")
                Exit Try
            End If
            GoTo PortRetry
        End Try
        'IPv4 NAT 测试
        Dim NATType As String
        Dim STUNServerDomain As String = "stun.miwifi.com" '指定 STUN 服务器
        Log("[STUN] 指定的 STUN 服务器: " + STUNServerDomain)
        Try
            Dim STUNServerIP As String = Dns.GetHostAddresses(STUNServerDomain)(0).ToString() '解析 STUN 服务器 IP
            Log("[STUN] 解析目标 STUN 服务器 IP: " + STUNServerIP)
            Dim STUNServerEndPoint As IPEndPoint = New IPEndPoint(IPAddress.Parse(STUNServerIP), 3478) '设置 IPEndPoint

            STUNClient.ReceiveTimeout = 500 '设置超时
            Log("[STUN] 开始进行 NAT 测试")
            Dim STUNTestResult = STUNClient.Query(STUNServerEndPoint, STUNQueryType.ExactNAT, True) '进行 STUN 测试

            NATType = STUNTestResult.NATType.ToString()
            Log("[STUN] 本地 NAT 类型: " + NATType)
        Catch ex As Exception
            Log(ex, "[STUN] 进行 NAT 测试失败", LogLevel.Normal)
            NATType = "TestFailed"
        End Try

        'IPv6
        Dim IPv6Status As String = "Unsupported"
        Try
            For Each ip In NatDiscovery.GetIPAddresses()
                If ip.AddressFamily() = AddressFamily.InterNetworkV6 Then 'IPv6
                    If ip.IsIPv6LinkLocal() OrElse ip.IsIPv6SiteLocal() OrElse ip.IsIPv6Teredo() OrElse ip.IsIPv4MappedToIPv6() Then
                        Continue For
                    ElseIf ip.IsPublic() Then
                        Log("[IP] 检测到 IPv6 公网地址")
                        IPv6Status = "Public"
                        Exit For
                    ElseIf ip.IsPrivate() AndAlso Not IPv6Status = "Supported" Then
                        Log("[IP] 检测到 IPv6 支持")
                        IPv6Status = "Supported"
                        Continue For
                    End If
                End If
            Next
        Catch ex As Exception
            Log(ex, "[IP] 进行 IPv6 测试失败", LogLevel.Normal)
            IPv6Status = "Unknown"
        End Try

        Return {NATType, IPv6Status}
    End Function
#End Region

End Class
