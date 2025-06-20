﻿Public Class PageLinkLeft

    Private IsLoad As Boolean = False
    Private IsPageSwitched As Boolean = False '如果在 Loaded 前切换到其他页面，会导致触发 Loaded 时再次切换一次
    Private Sub PageLinkLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True
        '切换默认页面
        If IsPageSwitched Then Exit Sub
        ItemLobby.SetChecked(True, False, False)
    End Sub
    Private Sub PageOtherLeft_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        IsPageSwitched = False
    End Sub

#Region "页面切换"

    ''' <summary>
    ''' 当前页面的编号。
    ''' </summary>
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.LinkLobby

    ''' <summary>
    ''' 勾选事件改变页面。
    ''' </summary>
    Private Sub PageCheck(sender As MyListItem, e As RouteEventArgs) Handles ItemLobby.Check, ItemIoi.Check, ItemSetup.Check, ItemHelp.Check, ItemFeedback.Check, ItemNetStatus.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        '若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1)
        If ID = -1 Then ID = PageID
        Select Case ID
            Case 0, FormMain.PageSubType.LinkLobby
                If FrmLinkLobby Is Nothing Then FrmLinkLobby = New PageLinkLobby
                Return FrmLinkLobby
            Case FormMain.PageSubType.LinkIoi
                If FrmLinkIoi Is Nothing Then FrmLinkIoi = New PageLinkIoi
                Return FrmLinkIoi
            Case FormMain.PageSubType.LinkSetup
                If FrmSetupLink Is Nothing Then FrmSetupLink = New PageSetupLink
                Return FrmSetupLink
            Case FormMain.PageSubType.LinkHelp
                Dim page = New PageOtherHelpDetail
                Dim panel = New StackPanel With {
                    .Orientation = Orientation.Vertical
                }
                Dim card = New MyCard With {
                    .Height = 200
                }
                panel.Children.Add(card)
                Dim textblock As New TextBlock With {
                    .Text = "暂时没写好 qwq",
                    .FontSize = 20,
                    .HorizontalAlignment = HorizontalAlignment.Center,
                    .VerticalAlignment = VerticalAlignment.Center
                }
                card.Children.Add(textblock)
                page.PanCustom.Children.Add(panel)
                If FrmLinkHelp Is Nothing Then FrmLinkHelp = page
                Return FrmLinkHelp
            Case FormMain.PageSubType.LinkFeedback
                If FrmLinkFeedback Is Nothing Then FrmLinkFeedback = New PageLinkFeedback
                Return FrmLinkFeedback
            Case FormMain.PageSubType.LinkNetStatus
                If FrmLinkNetStatus Is Nothing Then FrmLinkNetStatus = New PageLinkNetStatus
                Return FrmLinkNetStatus
            Case Else
                Throw New Exception("未知的更多子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        If PageID = ID Then Return
        AniControlEnabled += 1
        IsPageSwitched = True
        Try
            PageChangeRun(PageGet(ID))
            PageID = ID
        Catch ex As Exception
            Log(ex, "切换分页面失败（ID " & ID & "）", LogLevel.Feedback)
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
                         AaCode(Sub()
                                    CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                                    FrmMain.PanMainRight.Child = FrmMain.PageRight
                                    FrmMain.PageRight.Opacity = 0
                                End Sub, 130),
                         AaCode(Sub()
                                    '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                                    FrmMain.PageRight.Opacity = 1
                                    FrmMain.PageRight.PageOnEnter()
                                End Sub, 30, True)
                     }, "PageLeft PageChange")
    End Sub

#End Region

    Public Sub Reset(sender As Object, e As EventArgs)
        If MyMsgBox("是否要初始化联机页的所有设置？该操作不可撤销。", "初始化确认",, "取消", IsWarn:=True) = 1 Then
            If IsNothing(FrmSetupLink) Then FrmSetupLink = New PageSetupLink
            FrmSetupLink.Reset()
            ItemSetup.Checked = True
        End If
    End Sub

    Public Sub Recheck(sender As Object, e As EventArgs)
        If IsNothing(FrmLinkNetStatus) Then FrmLinkNetStatus = New PageLinkNetStatus
        Hint("正在重新检测网络环境，请稍后...")
        FrmLinkNetStatus.NetStatusTest()
        ItemNetStatus.Checked = True
    End Sub
    Public Sub NetStatusUpdate(Status As String)
        ItemNetStatus.Title = Status
    End Sub

End Class
