﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Data;
using System.Data.SQLite;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.Win32;
using WinForm = System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DoubleX.Infrastructure.Utility;
using DoubleXUI.Controls;

namespace DoubleX.Upload
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class Main : DxWindow
    {
        #region 信息属性

        /// <summary>
        /// FTP信息
        /// </summary>
        private FTPClientUtility ftpUtil;

        /// <summary>
        /// 系统参数(小写)
        /// </summary>
        private string[] systemParam { get { return new string[] { "id", "filefullpath", "filesize", "serverfilefullpath", "extension", "updatetime" }; } }

        /// <summary>
        /// 信息参数(FTP发送前)
        /// </summary>
        private List<RequestParamModel> beforeParam { get; set; }

        /// <summary>
        /// 信息参数(FTP发送后)
        /// </summary>
        public List<RequestParamModel> afterParam { get; set; }


        /// <summary>
        /// 任务数据源
        /// </summary>
        private List<TaskPathModel> taskPathSource { get; set; }

        /// <summary>
        /// 日志文本框滚动条是否在最下方
        /// true:文本框竖直滚动条在文本框最下面时，可以在文本框后追加日志
        /// false:当用户拖动文本框竖直滚动条，使其不在最下面时，即用户在查看旧日志，此时不添加新日志，
        /// </summary>
        public bool IsVerticalScrollBarAtBottom
        {
            get
            {
                //bool atBottom = false;

                //this.txtLog.Dispatcher.Invoke((Action)delegate
                //{
                //    //if (this.txtLog.VerticalScrollBarVisibility != ScrollBarVisibility.Visible)
                //    //{
                //    //    atBottom= true;
                //    //    return;
                //    //}
                //    double dVer = this.txtLog.VerticalOffset;       //获取竖直滚动条滚动位置
                //    double dViewport = this.txtLog.ViewportHeight;  //获取竖直可滚动内容高度
                //    double dExtent = this.txtLog.ExtentHeight;      //获取可视区域的高度

                //    if (dVer + dViewport >= dExtent)
                //    {
                //        atBottom = true;
                //    }
                //    else
                //    {
                //        atBottom = false;
                //    }
                //});

                //return atBottom;
                return false;
            }
        }

        /// <summary>
        /// 授权信息
        /// </summary>
        private LicenseFileModel licenseFileModel { get; set; }

        /// <summary>
        /// 授权统计
        /// </summary>
        private LicenseStatModel licenseStatModel { get; set; }


        //上传任务
        private volatile bool isUpload = false;
        private volatile bool isAfresh = false;                 //是否重新上传的任务
        private System.Threading.Thread uploadThread = null;

        #endregion

        public Main()
        {
            InitializeComponent();
            VerifyConfig();
            beforeParam = new List<RequestParamModel>();
            afterParam = new List<RequestParamModel>();
            InitPostParams();
            BindTaskPathSource();
            BindTaskList();
            Loading();
            this.Loaded += Main_Loaded;
        }

        private void Main_Loaded(object sender, RoutedEventArgs e)
        {
            ControlUtil.ExcuteAction(this, () =>
            {
                LastVerision();
            });
        }

        #region FTP连接/断开/浏览/注册/帮助

        private void btnConnectOpen_Click(object sender, RoutedEventArgs e)
        {
            ftpOpen();
        }

        private void btnConnectClose_Click(object sender, RoutedEventArgs e)
        {
            ftpClose();
        }

        private void btnFTPServerView_Click(object sender, RoutedEventArgs e)
        {
            FileView win = new FileView(ftpUtil);
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;// FormStartPosition.CenterParent;
            win.ShowDialog();
        }

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            Register win = new Register();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;// FormStartPosition.CenterParent;
            win.ShowDialog();
        }

        private void btnHelper_Click(object sender, RoutedEventArgs e)
        {
            string helpPath = AppDomain.CurrentDomain.BaseDirectory + "Help";
            System.Diagnostics.Process.Start("explorer.exe", helpPath);
        }

        private void ftpOpen()
        {
            ftpUtil = new FTPClientUtility(txtAddress.Text, txtName.Text, txtPassword.Text, IntHelper.Get(txtPort.Text), txtDirectory.Text);
            ControlUtil.ExcuteAction(this, () =>
            {
                SetFTPControlStatus(false);
            });
            try
            {
                WriteLog(string.Format("正在连接FTP：地址：{0} {1}：{2} 登录名：{3}", txtAddress.Text, txtDirectory.Text, txtPort.Text, txtName.Text));
                ftpUtil.Open();
                WriteLog(string.Format("FTP连接成功：地址：{0} {1}：{2}", txtAddress.Text, txtDirectory.Text, txtPort.Text), UILogType.Success);

                ControlUtil.ExcuteAction(this, () =>
                {
                    SetFTPControlStatus(true);
                });
            }
            catch (Exception ex)
            {
                WriteLog(string.Format("FTP连接失败：{0}", ExceptionHelper.GetMessage(ex)), UILogType.Error);

                ControlUtil.ExcuteAction(this, () =>
                {
                    SetFTPControlStatus(false);
                });
            }
        }

        private void ftpClose()
        {
            if (ftpUtil != null)
            {
                ftpUtil.Close();
                ftpUtil = null;
            }
            ControlUtil.ExcuteAction(this, () =>
            {
                SetFTPControlStatus(false);
                StopTask();
            });
            WriteLog(string.Format("FTP连接断开：地址：{0} {1}：{2}", txtAddress.Text, txtDirectory.Text, txtPort.Text), UILogType.Warning);
        }

        #endregion

        #region 文件/文件夹选择事件

        private void btnOpenFileDialog_Click(object sender, RoutedEventArgs e)
        {
            tabMain.SelectedIndex = 0;
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Multiselect = true;
            //dlg.DefaultExt = ".txt";
            //dlg.Filter = "Text documents (.txt)|*.txt";
            Nullable<bool> result = dlg.ShowDialog();
            if (result == true)
            {
                //单文件选择
                //BindTaskPathSource(EnumPathType.文件, dlg.FileName);

                //多文件选择
                foreach (var item in dlg.FileNames)
                {
                    BindTaskPathSource(EnumPathType.文件, item);
                }
            }
        }

        private void btnOpenFolderDialog_Click(object sender, RoutedEventArgs e)
        {
            tabMain.SelectedIndex = 0;
            WinForm.FolderBrowserDialog m_Dialog = new WinForm.FolderBrowserDialog();
            WinForm.DialogResult result = m_Dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }

            var folderPath = m_Dialog.SelectedPath.ToLower().Trim();
            BindTaskPathSource(EnumPathType.文件夹, folderPath);

            //new Thread(() =>
            //{
            //    var model = taskPathSource.FirstOrDefault(x => x.ItemPath.ToLower() == folderPath);
            //    CalculateTaskPathCount(model, folderPath);
            //    model.IsStatistical = true;
            //    ControlUtil.DataGridSyncBinding(gridTaskPathList, taskPathSource);
            //}).Start();

            Thread thread = new Thread(new ThreadStart(() =>
            {
                var model = taskPathSource.FirstOrDefault(x => x.ItemPath.ToLower() == folderPath);
                CalculateTaskPathCount(model, folderPath);
                model.IsStatistical = true;
                ControlUtil.ExcuteAction(this, () =>
                {
                    ControlUtil.DataGridSyncBinding(gridTaskPathList, taskPathSource);
                });
                System.Windows.Threading.Dispatcher.Run();
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

        }

        private void gridTaskPathDelete_Click(object sender, RoutedEventArgs e)
        {
            if (gridTaskPathList.SelectedItem != null)
            {
                TaskPathModel current = gridTaskPathList.SelectedItem as TaskPathModel;
                taskPathSource.Remove(taskPathSource.Find(x => x.ItemPath == current.ItemPath));
                ControlUtil.ExcuteAction(this, () =>
                {
                    ControlUtil.DataGridSyncBinding(gridTaskPathList, taskPathSource);
                });
            }
        }


        #endregion

        #region 任务(开始，结束，进度、结果,重新上传(仅待上传/错误) )

        private void btnTaskRunning_Click(object sender, RoutedEventArgs e)
        {
            if (taskPathSource == null || (taskPathSource != null && taskPathSource.Count() == 0))
            {
                ControlUtil.ShowMsg("请选择 文件夹 或 文件");
                return;
            }
            if (taskPathSource.Count(x => !x.IsStatistical) > 0)
            {
                ControlUtil.ShowMsg("正在计算文件夹文件数");
                return;
            }
            if (ftpUtil == null || (ftpUtil != null && !ftpUtil.IsConnection))
            {
                ControlUtil.ShowMsg("FTP信息 未连接 或 连接失败");
                return;
            }

            MessageBoxResult dr = ControlUtil.ShowMsg("任务运行其间，不允许期它操作，是否开始任务？", btn: MessageBoxButton.OKCancel, icon: MessageBoxImage.Question);
            if (dr == MessageBoxResult.OK)
            {
                var beforeModel = GetBeforeModel();
                var afterModel = GetAfterModel();
                var databaseModel = GetDatabaseSettingModel();
                var setting = GetTaskSettingModel();
                string taskId = Guid.NewGuid().ToString().ToLower();

                uploadThread = new Thread(new ThreadStart(() =>
                {
                    InitTask(taskId, beforeModel, afterModel, databaseModel, setting);
                }));
                uploadThread.Start();

            }
        }

        private void TaskView_AfreshTask(object sender, AfreshTaskEventArgs e)
        {
            var task = e.Task;
            if (task == null)
            {
                return;
            }

            isAfresh = true;
            string taskId = task.Id;
            RequestModel beforeModel = null, afterModel = null;
            DatabaseSettingModel databaseModel = null;
            TaskSettingModel setting = null;

            #region 参数重新设置

            if (!string.IsNullOrEmpty(task.BeforeJSON))
            {
                beforeModel = JsonHelper.Deserialize<RequestModel>(task.BeforeJSON);
                SetBeforeModel(beforeModel);
            }
            if (!string.IsNullOrEmpty(task.AfterJSON))
            {
                afterModel = JsonHelper.Deserialize<RequestModel>(task.AfterJSON);
                SetAfterModel(afterModel);
            }
            if (!string.IsNullOrEmpty(task.DBSettingJSON))
            {
                databaseModel = JsonHelper.Deserialize<DatabaseSettingModel>(task.DBSettingJSON);
                SetDatabaseModel(databaseModel);
            }
            if (!string.IsNullOrEmpty(task.SettingJSON))
            {
                setting = JsonHelper.Deserialize<TaskSettingModel>(task.SettingJSON);
                SetSettingModel(setting);
            }
            if (!string.IsNullOrEmpty(task.PathJSON))
            {
                taskPathSource = JsonHelper.Deserialize<List<TaskPathModel>>(task.PathJSON);
                SetTaskPath(taskPathSource);
            }

            #endregion

            uploadThread = new Thread(new ThreadStart(() =>
            {
                InitTask(task.Id, beforeModel, afterModel, databaseModel, setting, task);
            }));
            uploadThread.Start();
        }

        private void btnTaskStop_Click(object sender, RoutedEventArgs e)
        {
            StopTask();
        }

        #endregion

        #region 上传记录历史记录

        private void btnClearTask_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult dr = System.Windows.MessageBox.Show("是否清空操作记录(任务+上传)", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (dr == MessageBoxResult.OK)
            {
                DeleteTask(null);
                //StopTask();
                BindTaskList();
            }
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult dr = System.Windows.MessageBox.Show("是否在清空操作日志", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (dr == MessageBoxResult.OK)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    spLoggin.Children.Clear();
                });
            }
        }

        private void gridTaskList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender != null)
            {
                DataGrid grid = sender as DataGrid;
                if (grid != null && grid.SelectedItems != null && grid.SelectedItems.Count == 1)
                {
                    var model = grid.SelectedItem as TaskEntity;
                    if (model == null)
                        return;

                    if (isUpload)
                    {

                        ControlUtil.ShowMsg("正在上传任务中(不允许操作)...", "提示", icon: MessageBoxImage.Information);
                        return;
                    }

                    TaskView win = new TaskView(model);
                    win.Owner = this;
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;// FormStartPosition.CenterParent;
                    win.AfreshTaskEvent += new TaskView.AfreshTaskHandler(TaskView_AfreshTask);
                    win.ShowDialog();
                }
            }
        }

        #endregion

        #region 参数设置相关操作

        private void btnConnectionHelper_Click(object sender, RoutedEventArgs e)
        {
            DatabaseHelper win = new DatabaseHelper();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;// FormStartPosition.CenterParent;
            win.ShowDialog();
        }

        private void chkBeforeEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            //chkBeforeErrorContinue.IsChecked = false;
        }

        private void btnPostBeforeAdd_Click(object sender, RoutedEventArgs e)
        {
            if (beforeParam.Count(x => x.Name.ToLower() == "") > 0)
            {
                ControlUtil.ShowMsg("请先设置参数Key不为''后再添加");
                return;
            }

            ControlUtil.ExcuteAction(this, () =>
            {
                beforeParam.Add(new RequestParamModel()
                {
                    Id = Guid.NewGuid(),
                    Name = "",
                    PType = "Value",
                    Descript = "",
                    DefaultValue = "",
                    IsCanDelete = true
                });
                ControlUtil.DataGridSyncBinding(gridRequestBefore, beforeParam);
            });
        }

        private void gridRequestBefore_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            RequestParamModel oldModel = gridRequestBefore.SelectedItem as RequestParamModel;
            TextBox editingElement = e.EditingElement as TextBox;
            string newValue = string.Empty;
            string field = ((Binding)(e.Column as DataGridBoundColumn).Binding).Path.Path;
            if (editingElement != null)
            {
                newValue = editingElement.Text;
            }

            if (newValue != null && oldModel != null)
            {
                //系统参数，还原
                if (systemParam.Contains(oldModel.Name.ToLower()))
                {
                    switch (field.ToLower())
                    {
                        case "name":
                            editingElement.Text = oldModel.Name;
                            break;
                        case "ptype":
                            editingElement.Text = oldModel.PType;
                            break;
                        case "descript":
                            editingElement.Text = oldModel.Descript;
                            break;
                        case "defaultvalue":
                            editingElement.Text = oldModel.DefaultValue;
                            break;
                    }
                    ControlUtil.ShowMsg("系统参数不允许修改");
                    return;
                }

                //重复键，还原
                if (field.ToLower() == "name" && beforeParam.Count(x => x.Name.ToLower() == newValue.ToLower()) > 0)
                {
                    editingElement.Text = oldModel.Name;
                    ControlUtil.ShowMsg("己存在重复信息键Key");
                    return;
                }
            }
        }

        private void gridRequestBeforeDelete_Click(object sender, RoutedEventArgs e)
        {
            if (gridRequestBefore.SelectedItem != null)
            {
                RequestParamModel current = gridRequestBefore.SelectedItem as RequestParamModel;
                if (current != null && systemParam.Contains(current.Name.ToLower()))
                {

                    ControlUtil.ShowMsg("系统参数不允许删除");
                    return;
                }
                else
                {
                    beforeParam.Remove(beforeParam.Find(x => x.Id == current.Id));
                    ControlUtil.ExcuteAction(this, () =>
                    {
                        ControlUtil.DataGridSyncBinding(gridRequestBefore, beforeParam);
                    });
                }
            }
        }



        private void chkAfterEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            //chkAfterErrorContinue.IsChecked = false;
        }

        private void btnPostAfterAdd_Click(object sender, RoutedEventArgs e)
        {
            if (afterParam.Count(x => x.Name.ToLower() == "") > 0)
            {
                ControlUtil.ShowMsg("请先设置参数Key不为''后再添加");
                return;
            }

            ControlUtil.ExcuteAction(this, () =>
            {
                afterParam.Add(new RequestParamModel()
                {
                    Id = Guid.NewGuid(),
                    Name = "",
                    PType = "Return",
                    Descript = "",
                    DefaultValue = "",
                    IsCanDelete = true
                });
                ControlUtil.DataGridSyncBinding(gridRequestAfter, afterParam);
            });
        }

        private void gridRequestAfter_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            RequestParamModel oldModel = gridRequestAfter.SelectedItem as RequestParamModel;
            TextBox editingElement = e.EditingElement as TextBox;
            string newValue = string.Empty;
            string field = ((Binding)(e.Column as DataGridBoundColumn).Binding).Path.Path;
            if (editingElement != null)
            {
                newValue = editingElement.Text;
            }

            if (newValue != null && oldModel != null)
            {
                //系统参数，还原
                if (systemParam.Contains(oldModel.Name.ToLower()))
                {
                    switch (field.ToLower())
                    {
                        case "name":
                            editingElement.Text = oldModel.Name;
                            break;
                        case "ptype":
                            editingElement.Text = oldModel.PType;
                            break;
                        case "descript":
                            editingElement.Text = oldModel.Descript;
                            break;
                        case "defaultvalue":
                            editingElement.Text = oldModel.DefaultValue;
                            break;
                    }
                    ControlUtil.ShowMsg("系统参数不允许修改");
                    return;
                }

                //重复键，还原
                if (field.ToLower() == "name" && afterParam.Count(x => x.Name.ToLower() == newValue.ToLower()) > 0)
                {
                    editingElement.Text = oldModel.Name;
                    ControlUtil.ShowMsg("己存在重复信息键Key");
                    return;
                }
            }
        }

        private void gridRequestAfterDelete_Click(object sender, RoutedEventArgs e)
        {
            if (gridRequestAfter.SelectedItem != null)
            {
                RequestParamModel current = gridRequestAfter.SelectedItem as RequestParamModel;
                if (current != null && systemParam.Contains(current.Name.ToLower()))
                {
                    ControlUtil.ShowMsg("系统参数不允许删除");
                    return;
                }
                else
                {
                    afterParam.Remove(afterParam.Find(x => x.Id == current.Id));
                    ControlUtil.ExcuteAction(this, () =>
                    {
                        ControlUtil.DataGridSyncBinding(gridRequestAfter, afterParam);
                    });
                }
            }
        }

        #endregion

        #region 数据库发布操作

        private void btnScriptExcute_Click(object sender, RoutedEventArgs e)
        {
            var connectionString = txtConnectionStr.Text;
            var sql = txtSql.Text;
            if (VerifyHelper.IsEmpty(connectionString))
            {
                ControlUtil.ShowMsg("连接字符串不能为空");
                return;
            }
            if (VerifyHelper.IsEmpty(sql))
            {
                ControlUtil.ShowMsg("执行SQL语句不能为空");
                return;
            }

            int result = 0;

            try
            {
                if (raSQLserver.IsChecked.Value)
                {
                    result = SQLExecute("sqlserver", connectionString, sql);
                }
                if (raMySql.IsChecked.Value)
                {
                    result = SQLExecute("mysql", connectionString, sql);
                }
                if (raOracle.IsChecked.Value)
                {
                    result = SQLExecute("oracle", connectionString, sql);
                }
                if (raSQLite.IsChecked.Value)
                {
                    result = SQLExecute("sqlite", connectionString, sql);
                }
            }
            catch (Exception ex)
            {
                ControlUtil.ShowMsg(string.Format("出现错误：{0}", ExceptionHelper.GetMessage(ex)));
                return;
            }
            ControlUtil.ShowMsg(string.Format("执行成功：影响行数 {0}", result));

        }

        #endregion


        #region 辅助方法-任务参数

        /// <summary>
        /// 初始参数
        /// </summary>
        private void InitPostParams()
        {
            //请求前数据
            var beforeList = new List<RequestParamModel>();
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "Id",
                PType = "Guid",
                Descript = "上传文件的唯一标识",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileFullPath",
                PType = "string",
                Descript = "文件完整路径(含路径+文件名)",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileMD5",
                PType = "string",
                Descript = "文件MD5",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "ServerFileFullPath",
                PType = "string",
                Descript = "服务器文件完整路径",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileSize",
                PType = "long",
                Descript = "文件大小(KB)",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "Extension",
                PType = "string",
                Descript = "文件后缀",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "UpDateTime",
                PType = "string",
                Descript = "上传时间(yyyy-MM-dd HH:mm:ss)",
                DefaultValue = "",
                IsCanDelete = false
            });
            beforeParam = beforeList;
            gridRequestBefore.ItemsSource = beforeList;

            //请求后数据
            var afterList = new List<RequestParamModel>();
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "Id",
                PType = "Guid",
                Descript = "上传文件的唯一标识",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileFullPath",
                PType = "string",
                Descript = "文件完整路径(含路径+文件名)",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileMD5",
                PType = "string",
                Descript = "文件MD5",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "ServerFileFullPath",
                PType = "string",
                Descript = "服务器文件完整路径",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "FileSize",
                PType = "long",
                Descript = "文件大小(KB)",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "Extension",
                PType = "string",
                Descript = "文件后缀",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterList.Add(new RequestParamModel()
            {
                Id = Guid.NewGuid(),
                Name = "UpDateTime",
                PType = "string",
                Descript = "上传时间(yyyy-MM-dd HH:mm:ss)",
                DefaultValue = "",
                IsCanDelete = false
            });
            afterParam = afterList;
            gridRequestAfter.ItemsSource = afterList;

        }

        /// <summary>
        /// 请求前参数
        /// </summary>
        /// <returns></returns>
        private RequestModel GetBeforeModel()
        {
            RequestModel model = new RequestModel();
            model.IsEnable = chkBeforeEnabled.IsChecked == true ? true : false;
            model.Url = txtBeforeUrl.Text;
            model.Params = beforeParam;
            return model;
        }

        /// <summary>
        /// 请求后参数
        /// </summary>
        /// <returns></returns>
        private RequestModel GetAfterModel()
        {
            RequestModel model = new RequestModel();
            model.IsEnable = chkAfterEnabled.IsChecked == true ? true : false;
            model.Url = txtAfterUrl.Text;
            model.Params = afterParam;
            return model;
        }

        /// <summary>
        /// 数据库配置
        /// </summary>
        /// <returns></returns>
        public DatabaseSettingModel GetDatabaseSettingModel()
        {
            DatabaseSettingModel model = new DatabaseSettingModel();
            model.IsEnable = chkDatabaseEnabled.IsChecked == true ? true : false;
            model.DBType = "";
            if (raSQLserver.IsChecked.Value)
            {
                model.DBType = "sqlserver";
            }
            if (raMySql.IsChecked.Value)
            {
                model.DBType = "mysql";
            }
            if (raSQLite.IsChecked.Value)
            {
                model.DBType = "sqlite";
            }
            model.ConnectionStr = txtConnectionStr.Text;
            model.SQL = txtSql.Text;
            return model;
        }

        /// <summary>
        /// 任务设置配置
        /// </summary>
        /// <returns></returns>
        private TaskSettingModel GetTaskSettingModel()
        {
            TaskSettingModel model = new TaskSettingModel();
            model.RootPath = ftpUtil.ClientModel.Directory.EndsWith("/") ? ftpUtil.ClientModel.Directory : ftpUtil.ClientModel.Directory + "/";
            model.IsBefore = (chkBeforeEnabled.IsChecked == true ? true : false);
            model.IsAfter = (chkAfterEnabled.IsChecked == true ? true : false);
            model.IsErrorGoOn = (chkIsErrorGoOn.IsChecked == true ? true : false);
            return model;
        }


        private void SetBeforeModel(RequestModel model)
        {
            if (model != null)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    chkBeforeEnabled.IsChecked = model.IsEnable;
                    txtBeforeUrl.Text = model.Url;
                    beforeParam = model.Params;
                    ControlUtil.DataGridSyncBinding(gridRequestBefore, beforeParam);
                });
            }
        }

        private void SetAfterModel(RequestModel model)
        {
            if (model != null)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    chkAfterEnabled.IsChecked = model.IsEnable;
                    txtAfterUrl.Text = model.Url;
                    afterParam = model.Params;
                    ControlUtil.DataGridSyncBinding(gridRequestAfter, afterParam);
                });
            }
        }

        private void SetDatabaseModel(DatabaseSettingModel model)
        {
            if (model != null)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    chkDatabaseEnabled.IsChecked = model.IsEnable;
                    raSQLserver.IsChecked = false;
                    raMySql.IsChecked = false;
                    raSQLite.IsChecked = false;
                    switch (model.DBType)
                    {
                        case "sqlserver":
                            raSQLserver.IsChecked = true;
                            break;
                        case "mysql":
                            raMySql.IsChecked = true;
                            break;
                        case "sqlite":
                            raSQLite.IsChecked = true;
                            break;
                    }
                    txtConnectionStr.Text = model.ConnectionStr;
                    txtSql.Text = model.SQL;
                });
            }
        }

        private void SetSettingModel(TaskSettingModel model)
        {
            if (model != null)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    txtDirectory.Text = model.RootPath;
                    chkBeforeEnabled.IsChecked = model.IsBefore;
                    chkAfterEnabled.IsChecked = model.IsAfter;
                    chkIsErrorGoOn.IsChecked = model.IsErrorGoOn;
                });
            }
        }

        private void SetTaskPath(List<TaskPathModel> source)
        {
            if (source != null)
            {
                ControlUtil.ExcuteAction(this, () =>
                {
                    taskPathSource = source;
                    BindTaskPathSource();
                });
            }
        }

        #endregion

        #region 辅助方法-任务操作

        /// <summary>
        /// 初始任务
        /// </summary>
        private void InitTask(string taskId, RequestModel beforeModel, RequestModel afterModel, DatabaseSettingModel databaseModel, TaskSettingModel setting, TaskEntity taskEntity = null)
        {
            string url = AppHelper.GetConfig().WebUrl;
            isUpload = true; //线程信号

            try
            {
                SetCurrentRunningUI();
                if (isAfresh && taskEntity != null)
                {
                    AfreshTask(taskEntity, beforeModel, afterModel, databaseModel, setting);
                }
                else
                {
                    StartTask(taskId, beforeModel, afterModel, databaseModel, setting);
                }
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (ThreadAbortException ex)
            {
                //移除任务
                //DeleteTask(taskId);
                ControlUtil.ShowMsg("任务中止");
            }
            catch (LicenseException ex)
            {
                if (MessageBox.Show(string.Format("{0}(官方网站：{1})", ExceptionHelper.GetMessage(ex), url), "提示信息", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    System.Diagnostics.Process.Start("explorer.exe", url);
                    return;
                }
            }
            catch (Exception ex)
            {
                string exMsg = ExceptionHelper.GetMessage(ex);

                //设置任务状态
                UpdateTaskStatus(taskId, EnumTaskStatus.己中止);

                //状态栏
                WriteStatus("上传出错，任务中止");

                //写日志
                WriteLog(string.Format("上传出错，任务中止({0}) {1}", taskId, exMsg), UILogType.Error);

                ControlUtil.ShowMsg(exMsg, "错误", icon: MessageBoxImage.Error);
            }
            finally
            {
                SetCurrentFinishUI();
            }
        }

        /// <summary>
        ///  开始任务
        /// </summary>
        private void StartTask(string taskId, RequestModel beforeModel, RequestModel afterModel, DatabaseSettingModel databaseSettingModel, TaskSettingModel setting)
        {

            #region 增加任务数据

            WriteLog("正在创建任务");
            WriteStatus("正在创建任务");

            var taskEntity = new TaskEntity();
            taskEntity.Id = taskId;
            taskEntity.TaskName = DateTime.Now.ToString("yyyy-MM-dd HHmmsss");
            taskEntity.PathJSON = JsonHelper.Serialize(taskPathSource);
            taskEntity.BeforeJSON = JsonHelper.Serialize(beforeModel);
            taskEntity.AfterJSON = JsonHelper.Serialize(afterModel);
            taskEntity.SettingJSON = JsonHelper.Serialize(setting);
            taskEntity.DBSettingJSON = JsonHelper.Serialize(databaseSettingModel);
            taskEntity.FileTotal = taskPathSource.Sum(x => x.ItemCount);
            taskEntity.SuccessTotal = 0;
            taskEntity.ErrorTotal = 0;
            taskEntity.ErrorJSON = JsonHelper.Serialize(new List<TaskErrorModel>());
            taskEntity.Status = (int)EnumTaskStatus.未开始;
            taskEntity.CreateDt = DateTime.Now;
            if (InsertTask(taskEntity) == 0)
            {
                WriteLog(string.Format("任务创建失败({0})", taskEntity.TaskName), UILogType.Error);
                return;
            }
            else
            {
                WriteLog(string.Format("任务创建成功({0})", taskEntity.TaskName), UILogType.Success);
            }

            #endregion

            #region 增加待上传文件数据

            WriteLog("正在添加文件数据信息");
            WriteStatus("正在添加文件数据信息");

            //更新任务记录数据库配置置
            string destDbPath = CopyFileLogTempateDB(taskId, true);
            setting.FileDatabasePath = destDbPath.Replace(AppHelper.DatabasePath, "");
            taskEntity.SettingJSON = JsonHelper.Serialize(setting);
            UpdateTaskSetting(taskEntity);


            //处理待上传文件
            long optFileTotal = IsertTaskPaths(destDbPath, taskEntity, taskPathSource, setting);
            if (optFileTotal == taskEntity.FileTotal)
            {
                WriteLog(string.Format("文件数据添加成功 ({0})", taskEntity.TaskName), UILogType.Success);
            }
            else
            {
                WriteLog(string.Format("文件数据添加失败 {0}/{1} ({2})", taskEntity.FileTotal, optFileTotal, taskEntity.TaskName), UILogType.Error);
                return;
            }

            #endregion

            #region 同步任务

            BindTaskList();

            #endregion

            #region 上传文件操作(原代码)

            //WriteLog("正在上传文件");
            //WriteStatus("正在上传文件");

            ////设置任务状态
            //UpdateTaskStatus(taskEntity.Id, EnumTaskStatus.进行中);

            ////数据库操作配置
            //DatabaseSettingModel databaseModel = null;
            //if (!VerifyHelper.IsEmpty(taskEntity.DBSettingJSON))
            //{
            //    databaseModel = JsonConvert.DeserializeObject<DatabaseSettingModel>(taskEntity.DBSettingJSON);
            //}
            //int currentUploadFileTotal = 0, currentIndex = 0;
            //do
            //{
            //    var table = GetUploadFiles(destDbPath, taskEntity);
            //    if (table != null)
            //    {
            //        currentUploadFileTotal = currentUploadFileTotal + table.Rows.Count;
            //    }
            //    foreach (DataRow row in table.Rows)
            //    {
            //        var taskFileEntity = AppHelper.GetTaskFileEntityByRow(row);
            //        if (row != null && taskFileEntity != null && !string.IsNullOrWhiteSpace(taskFileEntity.FilePath))
            //        {
            //            try
            //            {
            //                //上传文件前调用接口
            //                BeforeApiRequest(destDbPath, taskEntity, taskFileEntity);

            //                //上传文件
            //                try
            //                {
            //                    ftpUtil.Upload(taskFileEntity.FilePath, taskFileEntity.ServerFullPath, 200, (current) =>
            //                    {
            //                        WriteStatus(string.Format("正在上传文件：{0} ({1}/{2})", taskFileEntity.FileName, current, taskFileEntity.FileSize));
            //                    });
            //                }
            //                catch (Exception ex)
            //                {
            //                    if (!setting.IsErrorGoOn)
            //                    {
            //                        throw ex;
            //                    }
            //                }

            //                //上传文件后调用接口
            //                AfterApiRequest(destDbPath, taskEntity, taskFileEntity);

            //                //上传文件后数据库执行
            //                if (databaseModel != null && databaseModel.IsEnable)
            //                {
            //                    SQLExecute(databaseModel.DBType, databaseModel.ConnectionStr, ReplaceSqlTag(databaseModel.SQL, taskEntity, taskFileEntity));
            //                }

            //                //设置文件状态
            //                UpdateTaskFileStatus(destDbPath, taskEntity, taskFileEntity, EnumTaskFileStatus.完成);

            //                //增加任务成功记录
            //                UpdateTaskSuccess(taskEntity, taskFileEntity);

            //                //同步任务
            //                BindTaskList();
            //            }
            //            catch (ThreadAbortException ex)
            //            {
            //                throw ex;
            //            }
            //            catch (Exception ex)
            //            {
            //                //异常消息
            //                string exMsg = ExceptionHelper.GetMessage(ex);

            //                //写日志
            //                WriteLog(string.Format("文件: {0}({1}) 上传出错", taskFileEntity.FileName, taskFileEntity.Id), UILogType.Error);

            //                //设置文件状态
            //                UpdateTaskFileStatus(destDbPath, taskEntity, taskFileEntity, EnumTaskFileStatus.出错);

            //                //只加错误，继续运行
            //                UpdateTaskError(taskEntity, taskFileEntity, setting, exMsg);

            //                //同步任务
            //                BindTaskList();

            //                if (!setting.IsErrorGoOn)
            //                {
            //                    //由外部修改状态,并结束
            //                    throw new Exception(string.Format("出现错误，任务中止 {0}", exMsg));
            //                }
            //            }
            //        }
            //        currentIndex++;
            //    }
            //} while (currentUploadFileTotal < taskEntity.FileTotal);

            //string overMsg = string.Format("文件上传完成：总数：{0}，成功：{1}，失败：{2}", taskEntity.FileTotal, taskEntity.SuccessTotal, taskEntity.ErrorTotal);

            ////设置任务状态
            //UpdateTaskStatus(taskEntity.Id, EnumTaskStatus.己完成);

            ////状态栏
            //WriteStatus(overMsg);

            ////写日志
            //WriteLog(string.Format("{0} 任务({1}/{2})", overMsg, taskEntity.Id, taskEntity.TaskName), taskEntity.ErrorTotal > 0 ? UILogType.Error : UILogType.Success);

            ////同步任务
            //BindTaskList();

            ////设置结束
            //SetCurrentFinishUI();

            #endregion

            UploadFile(taskEntity, beforeModel, afterModel, databaseSettingModel, setting, destDbPath);
        }

        /// <summary>
        /// 重新任务
        /// </summary>
        private void AfreshTask(TaskEntity taskEntity, RequestModel beforeModel, RequestModel afterModel, DatabaseSettingModel databaseSettingModel, TaskSettingModel setting)
        {
            //更新任务记录数据库配置置
            string destDbPath = CopyFileLogTempateDB(taskEntity.Id, false);
            //重置任务状态
            ModifyUploadFileStatus(destDbPath, taskEntity);
            //上传文件
            UploadFile(taskEntity, beforeModel, afterModel, databaseSettingModel, setting, destDbPath);
        }

        /// <summary>
        /// 结束任务
        /// </summary>
        private void StopTask()
        {
            SetCurrentFinishUI();

            if (uploadThread != null)
            {
                uploadThread.Abort();
                try
                {
                    while (!((uploadThread.ThreadState & ThreadState.Aborted) != 0 || (uploadThread.ThreadState & ThreadState.AbortRequested) != 0))
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("退出");
                }
                finally
                {
                    uploadThread = null;
                }
            }

        }

        /// <summary>
        /// 上传文件
        /// </summary>
        private void UploadFile(TaskEntity taskEntity, RequestModel beforeModel, RequestModel afterModel, DatabaseSettingModel databaseSettingModel, TaskSettingModel setting, string destDbPath)
        {

            WriteLog("正在上传文件");
            WriteStatus("正在上传文件");

            //设置任务状态
            UpdateTaskStatus(taskEntity.Id, EnumTaskStatus.进行中);

            //数据库操作配置
            DatabaseSettingModel databaseModel = null;
            if (!VerifyHelper.IsEmpty(taskEntity.DBSettingJSON))
            {
                databaseModel = JsonConvert.DeserializeObject<DatabaseSettingModel>(taskEntity.DBSettingJSON);
            }
            int currentUploadFileTotal = 0, currentIndex = 0, dataTotal = GetUploadFilesTotal(taskEntity.Id, destDbPath, EnumTaskFileStatus.待上传);
            if (dataTotal > 0)
            {
                do
                {
                    var table = GetUploadFiles(destDbPath, taskEntity);
                    if (table != null)
                    {
                        currentUploadFileTotal = currentUploadFileTotal + table.Rows.Count;

                        foreach (DataRow row in table.Rows)
                        {
                            var taskFileEntity = AppHelper.GetTaskFileEntityByRow(row);
                            if (row != null && taskFileEntity != null && !string.IsNullOrWhiteSpace(taskFileEntity.FilePath))
                            {
                                try
                                {
                                    //上传文件前调用接口
                                    BeforeApiRequest(destDbPath, taskEntity, taskFileEntity);

                                    //上传文件
                                    try
                                    {
                                        ftpUtil.Upload(taskFileEntity.FilePath, taskFileEntity.ServerFullPath, 200, (current) =>
                                        {
                                            WriteStatus(string.Format("正在上传文件：{0} ({1}/{2})", taskFileEntity.FileName, current, taskFileEntity.FileSize));
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        if (!setting.IsErrorGoOn)
                                        {
                                            throw ex;
                                        }
                                    }

                                    //上传文件后调用接口
                                    AfterApiRequest(destDbPath, taskEntity, taskFileEntity);

                                    //上传文件后数据库执行
                                    if (databaseModel != null && databaseModel.IsEnable)
                                    {
                                        SQLExecute(databaseModel.DBType, databaseModel.ConnectionStr, ReplaceSqlTag(databaseModel.SQL, taskEntity, taskFileEntity));
                                    }

                                    //设置文件状态
                                    UpdateTaskFileStatus(destDbPath, taskEntity, taskFileEntity, EnumTaskFileStatus.完成);

                                    //增加任务成功记录
                                    UpdateTaskSuccess(taskEntity, taskFileEntity);

                                    //同步任务
                                    BindTaskList();
                                }
                                catch (ThreadAbortException ex)
                                {
                                    throw ex;
                                }
                                catch (Exception ex)
                                {
                                    //异常消息
                                    string exMsg = ExceptionHelper.GetMessage(ex);

                                    //写日志
                                    WriteLog(string.Format("文件: {0}({1}) 上传出错", taskFileEntity.FileName, taskFileEntity.Id), UILogType.Error);

                                    //设置文件状态
                                    UpdateTaskFileStatus(destDbPath, taskEntity, taskFileEntity, EnumTaskFileStatus.出错);

                                    //只加错误，继续运行
                                    UpdateTaskError(taskEntity, taskFileEntity, setting, exMsg);

                                    //同步任务
                                    BindTaskList();

                                    if (!setting.IsErrorGoOn)
                                    {
                                        //由外部修改状态,并结束
                                        throw new Exception(string.Format("出现错误，任务中止 {0}", exMsg));
                                    }
                                }
                            }
                            currentIndex++;
                        }
                    }
                } while (currentUploadFileTotal < dataTotal);
            }


            string overMsg = string.Format("文件上传完成：总数：{0}，成功：{1}，失败：{2}", dataTotal, taskEntity.SuccessTotal, taskEntity.ErrorTotal);

            //设置任务状态(己完成,并同步结果数据,新方法SyncTaskResult中同时设置状态了)
            //UpdateTaskStatus(taskEntity.Id, EnumTaskStatus.己完成);
            SyncTaskResult(taskEntity.Id, destDbPath);

            //状态栏
            WriteStatus(overMsg);

            //写日志
            WriteLog(string.Format("{0} 任务({1}/{2})", overMsg, taskEntity.Id, taskEntity.TaskName), taskEntity.ErrorTotal > 0 ? UILogType.Error : UILogType.Success);

            //同步任务
            BindTaskList();

            //设置结束
            SetCurrentFinishUI();
        }

        /// <summary>
        /// 设置当前界面运行状态（禁用控件）
        /// </summary>
        private void SetCurrentRunningUI()
        {
            ControlUtil.ExcuteAction(this, () =>
            {
                btnTaskRunning.Visibility = Visibility.Collapsed;
                btnTaskStop.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 设置当前界面结束状态（启用控件）
        /// </summary>
        private void SetCurrentFinishUI()
        {
            isUpload = false;
            isAfresh = false;

            BindTaskList();

            ControlUtil.ExcuteAction(this, () =>
            {
                btnTaskRunning.Visibility = Visibility.Visible;
                btnTaskStop.Visibility = Visibility.Collapsed;
                tbStatus.Text = "未有任务运行....";
            });
        }

        #endregion

        #region 辅助方法-接口调用

        /// <summary>
        /// 上传文件前调用接口
        /// </summary>
        /// <param name="dbPath"></param>
        /// <param name="taskEntity"></param>
        /// <param name="taskFileEntity"></param>
        /// <returns></returns>
        private void BeforeApiRequest(string dbPath, TaskEntity taskEntity, TaskFileEntity taskFileEntity)
        {
            var berforeSetting = JsonHelper.Deserialize<RequestModel>(taskEntity.BeforeJSON);
            if (!berforeSetting.IsEnable)
            {
                return;
            }
            string result = GetHttp(berforeSetting.Url, GetHttpPostByRequestParams(taskEntity, taskFileEntity));
            if (!string.IsNullOrWhiteSpace(result))
            {
                //false对象/false字符串
                if (result.ToLower().StartsWith("false") || result.ToLower().StartsWith("\"false"))
                {
                    throw new Exception("api before result is " + result.ToLower());
                }
                //taskFileEntity.BeforeResult = result;
                UpdateApiBeforeResultObj(dbPath, taskFileEntity, result);
            }
            else
            {
                throw new Exception("api before result is empty");
            }
        }

        /// <summary>
        /// 上传文件后调用接口
        /// </summary>
        /// <param name="dbPath"></param>
        /// <param name="taskEntity"></param>
        /// <param name="taskFileEntity"></param>
        /// <returns></returns>
        private void AfterApiRequest(string dbPath, TaskEntity taskEntity, TaskFileEntity taskFileEntity)
        {
            var afterSetting = JsonHelper.Deserialize<RequestModel>(taskEntity.AfterJSON);
            if (!afterSetting.IsEnable)
            {
                return;
            }
            var resultObj = JsonHelper.Deserialize<JObject>(taskFileEntity.BeforeResult);
            string result = GetHttp(afterSetting.Url, GetHttpPostByRequestParams(taskEntity, taskFileEntity, isAfterParam: true, returnJosnObj: resultObj));
            if (!string.IsNullOrWhiteSpace(result))
            {
                //false对象/false字符串
                if (result.ToLower().StartsWith("false") || result.ToLower().StartsWith("\"false"))
                {
                    throw new Exception("api after result is " + result.ToLower());
                }
            }
            else
            {
                throw new Exception("api after result is empty");
            }
        }

        #endregion

        #region 辅助方法-数据库操作

        /// <summary>
        /// 添加新任务（数据）
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        private int InsertTask(TaskEntity entity)
        {
            if (entity == null) return 0;

            StringBuilder strSql = new StringBuilder();
            strSql.Append("insert into TB_Task(");
            strSql.Append("Id,TaskName,PathJSON,BeforeJSON,AfterJSON,SettingJSON,DBSettingJSON,FileTotal,SuccessTotal,ErrorTotal,ErrorJSON,Status,CreateDt)");
            strSql.Append(" values (");
            strSql.Append("@Id,@TaskName,@PathJSON,@BeforeJSON,@AfterJSON,@SettingJSON,@DBSettingJSON,@FileTotal,@SuccessTotal,@ErrorTotal,@ErrorJSON,@Status,@CreateDt)");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@TaskName", DbType.String),
                    new SQLiteParameter("@PathJSON", DbType.String),
                    new SQLiteParameter("@BeforeJSON",DbType.String),
                    new SQLiteParameter("@AfterJSON", DbType.String),
                    new SQLiteParameter("@SettingJSON", DbType.String),
                    new SQLiteParameter("@DBSettingJSON", DbType.String),
                    new SQLiteParameter("@FileTotal", DbType.Double),
                    new SQLiteParameter("@SuccessTotal", DbType.Double),
                    new SQLiteParameter("@ErrorTotal", DbType.Double),
                    new SQLiteParameter("@ErrorJSON", DbType.String),
                    new SQLiteParameter("@Status", DbType.Int32,4),
                    new SQLiteParameter("@CreateDt", DbType.DateTime)
            };
            parameters[0].Value = entity.Id;
            parameters[1].Value = entity.TaskName;
            parameters[2].Value = entity.PathJSON;
            parameters[3].Value = entity.BeforeJSON;
            parameters[4].Value = entity.AfterJSON;
            parameters[5].Value = entity.SettingJSON;
            parameters[6].Value = entity.DBSettingJSON;
            parameters[7].Value = entity.FileTotal;
            parameters[8].Value = entity.SuccessTotal;
            parameters[9].Value = entity.ErrorTotal;
            parameters[10].Value = entity.ErrorJSON;
            parameters[11].Value = entity.Status;
            parameters[12].Value = DateTime.Now;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters);
        }

        /// <summary>
        /// 处理上传的文件文件
        /// </summary>
        /// <param name="taskEntity"></param>
        /// <returns></returns>
        private long IsertTaskPaths(string destDbPath, TaskEntity taskEntity, List<TaskPathModel> pathSource, TaskSettingModel setting)
        {
            long total = 0;
            foreach (var item in pathSource)
            {
                if (item.ItemType == (int)EnumPathType.文件)
                {
                    InsertTaskFile(destDbPath, taskEntity, setting, item.ItemPath, "", ref total);
                }
                else if (item.ItemType == (int)EnumPathType.文件夹)
                {
                    var dicName = System.IO.Path.GetDirectoryName(item.ItemPath);
                    InsertTaskFolder(destDbPath, taskEntity, setting, item.ItemPath, dicName, ref total);
                }
            }
            return total;
        }

        /// <summary>
        /// 插入任务中的文件夹
        /// </summary>
        /// <param name="dbConnection"></param>
        /// <param name="taskEntity"></param>
        /// <param name="folderPath"></param>
        /// <param name="insertFileTotal"></param>
        private void InsertTaskFolder(string destDbPath, TaskEntity taskEntity, TaskSettingModel setting, string folderPath, string selectFolderPath, ref long insertFileTotal)
        {
            var folders = Directory.GetDirectories(folderPath);
            var files = Directory.GetFiles(folderPath);
            if (files != null)
            {
                foreach (var item in files)
                {
                    InsertTaskFile(destDbPath, taskEntity, setting, item, selectFolderPath, ref insertFileTotal);
                }
            }
            foreach (var dir in folders)
            {
                InsertTaskFolder(destDbPath, taskEntity, setting, dir, selectFolderPath, ref insertFileTotal);
            }

        }

        /// <summary>
        /// 插入任务文件数据
        /// </summary>
        /// <param name="dbConnection"></param>
        /// <param name="taskEntity"></param>
        /// <param name="filePath"></param>
        /// <param name="insertFileTotal"></param>
        private void InsertTaskFile(string destDbPath, TaskEntity taskEntity, TaskSettingModel setting, string filePath, string selectFolderPath, ref long insertFileTotal)
        {
            long currentTotal = insertFileTotal;

            #region 先增加任务数据记录

            var fileModel = new FileInfo(filePath);
            var fileSize = Math.Ceiling(fileModel.Length / 1024.0); //转KB,出现小数不管多少，向前进1，

            StringBuilder strSql = new StringBuilder();
            strSql.Append("insert into TB_Files(");
            strSql.Append("Id,TaskId,FilePath,FileSize,FileName,FileExtension,ServerFullPath,UpSize,BeforeResult,Status,LastDt)");
            strSql.Append(" values (");
            strSql.Append("@Id,@TaskId,@FilePath,@FileSize,@FileName,@FileExtension,@ServerFullPath,@UpSize,@BeforeResult,@Status,@LastDt)");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@TaskId", DbType.String),
                    new SQLiteParameter("@FilePath", DbType.String),
                    new SQLiteParameter("@FileSize",DbType.Double),
                    new SQLiteParameter("@FileName", DbType.String),
                    new SQLiteParameter("@FileExtension", DbType.String),
                    new SQLiteParameter("@ServerFullPath", DbType.String),
                    new SQLiteParameter("@UpSize", DbType.Double),
                    new SQLiteParameter("@BeforeResult", DbType.String),
                    new SQLiteParameter("@Status", DbType.Int32,4),
                    new SQLiteParameter("@LastDt", DbType.DateTime)
            };
            parameters[0].Value = Guid.NewGuid().ToString().ToLower();
            parameters[1].Value = taskEntity.Id;
            parameters[2].Value = filePath;
            parameters[3].Value = fileSize;
            parameters[4].Value = fileModel.Name;
            parameters[5].Value = fileModel.Extension;
            parameters[6].Value = GetServerFileFullPath(taskEntity, setting, fileModel, selectFolderPath);
            parameters[7].Value = 0;
            parameters[8].Value = "";
            parameters[9].Value = (int)EnumTaskFileStatus.待上传;
            parameters[10].Value = DateTime.Now;

            this.Dispatcher.Invoke(new Action(() =>
            {
                WriteStatus(string.Format("正在处理待上传文件数据：{0}/{1}", taskEntity.FileTotal, (currentTotal + 1)));
            }));

            if (SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), strSql.ToString(), CommandType.Text, parameters) == 0)
            {
                this.Dispatcher.Invoke(new Action(() =>
                {
                    WriteLog(string.Format("文件数据添加失败：{0}({1})", filePath, taskEntity.TaskName), UILogType.Error);
                }));
            }

            #endregion

            insertFileTotal++;

            if (licenseFileModel.IsTrial && fileSize > 100)
                throw new LicenseException("试用版文件大小不能超过100KB", LicenseExceptionType.授权试用错误);

        }


        /// <summary>
        /// 更新任务状态
        /// </summary>
        /// <returns></returns>
        private bool UpdateTaskStatus(string taskId, EnumTaskStatus status)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set Status=@Status where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@Status", DbType.Int32,4)
            };
            parameters[0].Value = taskId;
            parameters[1].Value = (int)status;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters) > 0;
        }

        /// <summary>
        /// 同步任务结果
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="destDbPath"></param>
        public void SyncTaskResult(string taskId, string destDbPath)
        {
            int fileTotal = GetUploadFilesTotal(taskId, destDbPath, null);
            int successTotal = GetUploadFilesTotal(taskId, destDbPath, EnumTaskFileStatus.完成);

            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set Status=@Status,FileTotal=@FileTotal,SuccessTotal=@SuccessTotal,ErrorTotal=@ErrorTotal where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@Status", DbType.Int32,4),
                    new SQLiteParameter("@FileTotal", DbType.Int32,4),
                    new SQLiteParameter("@SuccessTotal", DbType.Int32,4),
                    new SQLiteParameter("@ErrorTotal", DbType.Int32,4)
            };
            parameters[0].Value = taskId;
            parameters[1].Value = (int)EnumTaskStatus.己完成;
            parameters[2].Value = fileTotal;
            parameters[3].Value = successTotal;
            parameters[4].Value = fileTotal - successTotal;

            SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters);
        }

        /// <summary>
        /// 更新任务错误
        /// </summary>
        /// <param name="taskEntity"></param>
        /// <param name="taskFieldEntity"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        private bool UpdateTaskError(TaskEntity taskEntity, TaskFileEntity taskFielEntity, TaskSettingModel setting, string msg)
        {
            var taskErrorString = StringHelper.Get(SQLiteHelper.ExecuteScalar(AppHelper.GetTaskDatabaseConnectionStr(), "select ErrorJSON from TB_Task where Id='" + taskEntity.Id + "'", CommandType.Text));
            List<TaskErrorModel> errorList = new List<TaskErrorModel>();
            if (!string.IsNullOrWhiteSpace(taskErrorString))
            {
                errorList = JsonHelper.Deserialize<List<TaskErrorModel>>(taskErrorString);
            }
            errorList.Add(new TaskErrorModel()
            {
                TaskFileId = taskFielEntity.Id,
                FileFullPath = taskFielEntity.FilePath,
                MessageText = msg
            });

            taskEntity.ErrorJSON = JsonHelper.Serialize(errorList);
            taskEntity.ErrorTotal = errorList.Count;

            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set ErrorJSON=@ErrorJSON,ErrorTotal=ErrorTotal+1 where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@ErrorJSON", DbType.String)
            };
            parameters[0].Value = taskEntity.Id;
            parameters[1].Value = taskEntity.ErrorJSON;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters) > 0;
        }

        /// <summary>
        /// 设置任务成功信息
        /// </summary>
        /// <param name="taskEntity"></param>
        /// <param name="taskFielEntity"></param>
        /// <returns></returns>
        private bool UpdateTaskSuccess(TaskEntity taskEntity, TaskFileEntity taskFielEntity)
        {
            taskEntity.SuccessTotal = taskEntity.SuccessTotal + 1;

            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set SuccessTotal=SuccessTotal+1 where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String)
            };
            parameters[0].Value = taskEntity.Id;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters) > 0;
        }

        /// <summary>
        /// 更新任务配置
        /// </summary>
        /// <param name="taskEntit"></param>
        /// <returns></returns>
        private bool UpdateTaskSetting(TaskEntity taskEntity)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set SettingJSON=@SettingJSON where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@SettingJSON", DbType.String)
            };
            parameters[0].Value = taskEntity.Id;
            parameters[1].Value = taskEntity.SettingJSON;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters) > 0;
        }


        /// <summary>
        /// 更新文件请求前接口结果
        /// </summary>
        /// <param name="destDbPath"></param>
        /// <param name="taskFileId"></param>
        /// <param name="beforeJSON"></param>
        /// <returns></returns>
        private bool UpdateApiBeforeResultObj(string destDbPath, TaskFileEntity taskFileEntity, string beforeJSON)
        {
            taskFileEntity.BeforeResult = beforeJSON;
            //更新上传路径：
            ApiBeforeSysModel _sysModel = null;
            try { _sysModel = JsonConvert.DeserializeObject<ApiBeforeSysModel>(beforeJSON); } catch { }
            if (_sysModel != null && !string.IsNullOrWhiteSpace(_sysModel._NewServerPath))
            {
                taskFileEntity.ServerFullPath = _sysModel._NewServerPath;
            }


            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Files set BeforeResult=@BeforeResult,ServerFullPath=@ServerFullPath where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@BeforeResult", DbType.String),
                    new SQLiteParameter("@ServerFullPath", DbType.String)
            };
            parameters[0].Value = taskFileEntity.Id;
            parameters[1].Value = taskFileEntity.BeforeResult;
            parameters[2].Value = taskFileEntity.ServerFullPath;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), strSql.ToString(), CommandType.Text, parameters) > 0;
        }

        /// <summary>
        /// 更新文件状态
        /// </summary>
        /// <param name="taskEntity"></param>
        /// <param name="taskFieldEntity"></param>
        /// <param name="setting"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        private bool UpdateTaskFileStatus(string destDbPath, TaskEntity taskEntity, TaskFileEntity taskFielEntity, EnumTaskFileStatus status)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Files set Status=@Status where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String),
                    new SQLiteParameter("@Status", DbType.Int32,4)
            };
            parameters[0].Value = taskFielEntity.Id;
            parameters[1].Value = (int)status;

            return SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), strSql.ToString(), CommandType.Text, parameters) > 0;
        }

        /// <summary>
        /// 清空任务(taskId为空，清空所有)
        /// </summary>
        /// <returns></returns>
        private void DeleteTask(string taskId)
        {
            StringBuilder strSql = new StringBuilder();
            strSql.Append("delete from TB_Task ");
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                strSql.AppendFormat(" where Id='{0}'", taskId);
            }
            SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text);

            DeleteFileLogDatabase(taskId);
        }

        /// <summary>
        /// 获取待上传文件列表(每批50条数据)
        /// </summary>
        /// <param name="destDbPath"></param>
        /// <param name="taskEntity"></param>
        /// <returns></returns>
        private DataTable GetUploadFiles(string destDbPath, TaskEntity taskEntity)
        {
            string sql = string.Format("select * from  TB_Files where TaskId='{0}' and Status={1} order by LastDt limit 50 offset 0", taskEntity.Id, (int)EnumTaskFileStatus.待上传);
            DataSet ds = SQLiteHelper.ExecuteDataSet(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), sql, CommandType.Text);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                return ds.Tables[0];
            }
            return null;
        }

        /// <summary>
        /// 获取状态值文件数据总数
        /// </summary>
        /// <param name="destDbPath"></param>
        /// <param name="taskEntity"></param>
        /// <returns></returns>
        private int GetUploadFilesTotal(string taskId, string destDbPath, EnumTaskFileStatus? status)
        {
            string sql = string.Format("select count(*) from  TB_Files where TaskId='{0}' ", taskId);
            if (status != null)
            {
                sql += string.Format(" and Status={0} ", (int)status.Value);
            }
            object obj = SQLiteHelper.ExecuteScalar(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), sql, CommandType.Text);
            int total = 0;
            if (obj != null)
            {
                int.TryParse(obj.ToString(), out total);
            }
            return total;
        }

        /// <summary>
        /// 重置上传状态
        /// </summary>
        /// <param name="destDbPath"></param>
        /// <param name="taskEntity"></param>
        private void ModifyUploadFileStatus(string destDbPath, TaskEntity taskEntity)
        {
            string sql = string.Format("Update TB_Files set Status={1} where TaskId='{0}' and Status<>{2}", taskEntity.Id, (int)EnumTaskFileStatus.待上传, (int)EnumTaskFileStatus.完成);
            SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskFileDatabaseConnectionStr(destDbPath), sql, CommandType.Text);

            StringBuilder strSql = new StringBuilder();
            strSql.Append("Update TB_Task set ErrorJSON='',ErrorTotal=0 where Id=@Id");
            SQLiteParameter[] parameters = {
                    new SQLiteParameter("@Id", DbType.String)
            };
            parameters[0].Value = taskEntity.Id;

            SQLiteHelper.ExecuteNonQuery(AppHelper.GetTaskDatabaseConnectionStr(), strSql.ToString(), CommandType.Text, parameters);
        }

        /// <summary>
        /// 获取任务列表
        /// </summary>
        /// <returns></returns>
        private List<TaskEntity> GetTaskList()
        {
            List<TaskEntity> list = new List<TaskEntity>();
            string sql = string.Format("select * from  TB_Task order by CreateDt desc limit 30 offset 0");
            DataSet ds = SQLiteHelper.ExecuteDataSet(AppHelper.GetTaskDatabaseConnectionStr(), sql, CommandType.Text);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    var model = AppHelper.GetTaskEntityByRow(row);
                    if (model == null) { continue; }
                    list.Add(model);
                }
            }
            return list;
        }

        /// <summary>
        /// 从模版复制当前任务上传文件库
        /// </summary>
        /// <param name="taskName"></param>
        /// <returns></returns>
        private string CopyFileLogTempateDB(string taskId, bool isOverride = true)
        {
            string sourcePath = string.Format(@"{0}template.db", AppHelper.DatabasePath);
            string destDirPath = string.Format(@"{0}/temp/{1}/", AppHelper.DatabasePath, DateTime.Now.ToString("yyyyMM"));
            if (!Directory.Exists(destDirPath))
            {
                Directory.CreateDirectory(destDirPath);
            }
            string destFilePath = string.Format("{0}{1}.db", destDirPath, taskId);
            if (isOverride)
            {
                File.Copy(sourcePath, destFilePath, true);
            }
            if (!isOverride && !File.Exists(destFilePath))
            {
                File.Copy(sourcePath, destFilePath);
            }
            return destFilePath;
        }

        /// <summary>
        /// 移除任务文件
        /// </summary>
        /// <param name="taskName"></param>
        /// <returns></returns>
        private void DeleteFileLogDatabase(string taskId)
        {
            string tempPath = string.Format(@"{0}/temp/", AppHelper.DatabasePath);
            if (string.IsNullOrWhiteSpace(taskId))
            {
                if (Directory.Exists(tempPath))
                {
                    try
                    {
                        Directory.Delete(tempPath, true);
                    }
                    catch { }
                }
                return;
            }

            string fileNme = string.Format("{0}.db", taskId);
            var files = Directory.GetFiles(tempPath, fileNme);
            if (files != null && files.Length > 0 && File.Exists(files[0]))
            {
                try
                {
                    File.Delete(files[0]);
                }
                catch { }

            }
        }

        /// <summary>
        /// 执行数据库操作
        /// </summary>
        /// <param name="type"></param>
        /// <param name="connection"></param>
        /// <param name="sql"></param>
        /// <returns></returns>
        private int SQLExecute(string type, string connection, string sql)
        {
            if (VerifyHelper.IsEmpty(type) || VerifyHelper.IsEmpty(connection) || VerifyHelper.IsEmpty(sql))
            {
                throw new Exception("数据库操作参数错误（连接字符串/脚本）");
            }

            int result = 0;
            type = type.ToLower().Trim();

            if (type == "sqlserver")
            {
                result = SQLServerHelper.ExecuteNonQuery(connection, sql);
            }
            if (type == "mysql")
            {
                sql = sql.Replace("\\", "\\\\");
                result = MySqlHelper.ExecuteNonQuery(connection, sql);
            }
            if (type == "oracle")
            {
                result = OracleHelper.ExecuteNonQuery(connection, sql);
            }
            if (type == "sqlite")
            {
                result = SQLiteHelper.ExecuteNonQuery(connection, sql, CommandType.Text);
            }

            return result;
        }

        #endregion

        #region 辅助方法-工具方法

        /// <summary>
        /// 绑定上传任务路径数据源
        /// </summary>
        /// <param name="pathType">类型(EnumPathType)</param>
        /// <param name="path"></param>
        private void BindTaskPathSource(EnumPathType pathType = EnumPathType.默认, string path = null)
        {
            if (taskPathSource == null) { taskPathSource = new List<TaskPathModel>(); }

            if (pathType != EnumPathType.默认 && !string.IsNullOrEmpty(path))
            {
                path = path.ToLower();
                if (taskPathSource.Count(x => x.ItemPath == path) == 0)
                {
                    taskPathSource.Add(new TaskPathModel { ItemIndex = -1, ItemType = (int)pathType, ItemPath = path, ItemCount = (pathType == EnumPathType.文件 ? 1 : 0), IsStatistical = (pathType == EnumPathType.文件 ? true : false) });
                }
            }

            for (var i = 0; i < taskPathSource.Count; i++)
            {
                if (taskPathSource[i].ItemIndex == -1)
                {
                    taskPathSource[i].ItemIndex = i + 1;
                }
            }

            ControlUtil.ExcuteAction(this, () =>
            {
                ControlUtil.DataGridSyncBinding(gridTaskPathList, taskPathSource);
            });
        }

        private void BindTaskList()
        {
            ControlUtil.ExcuteAction(this, () =>
            {
                ControlUtil.DataGridSyncBinding(gridTaskList, GetTaskList());
            });
        }

        /// <summary>
        /// 计算文件总数
        /// </summary>
        /// <param name="model"></param>
        /// <param name="currentFolderPath"></param>
        private void CalculateTaskPathCount(TaskPathModel model, string currentFolderPath)
        {
            var folders = Directory.GetDirectories(currentFolderPath);
            var files = Directory.GetFiles(currentFolderPath);
            if (files != null)
            {
                model.ItemCount += files.Length;
                this.Dispatcher.Invoke(new Action(() =>
                {
                    ControlUtil.DataGridSyncBinding(gridTaskPathList, taskPathSource);
                }));
            }
            foreach (var dir in folders)
            {
                CalculateTaskPathCount(model, dir);
            }
        }

        /// <summary>
        /// 输出日志(界面 + 文本)
        /// </summary>
        /// <param name="msg"></param>
        private void WriteLog(string msg, UILogType logType = UILogType.Default)
        {
            ControlUtil.ExcuteAction(this, () =>
            {
                TextBlock msgBlock = new TextBlock();
                msgBlock.Text = string.Format("{0} {1}", DateTime.Now.ToString("HH:mm:sss"), msg);
                switch (logType)
                {
                    case UILogType.Success:
                        msgBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("Green"));
                        break;
                    case UILogType.Error:
                        msgBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("Red"));
                        break;
                    case UILogType.Warning:
                        msgBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFAE00"));
                        break;
                    default:
                        msgBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF34495E"));
                        break;
                }
                spLoggin.Children.Add(msgBlock);
            });

            #region 操作日志，滚动至底部

            //ThreadPool.QueueUserWorkItem(sender =>
            //{
            //    while (true)
            //    {
            //        this.txtLog.Dispatcher.BeginInvoke((Action)delegate
            //        {
            //            this.txtLog.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff\r\n"));
            //            if (IsVerticalScrollBarAtBottom)
            //            {
            //                this.txtLog.ScrollToEnd();
            //            }
            //        });
            //        Thread.Sleep(600);
            //    }
            //});

            #endregion
        }

        /// <summary>
        /// 操作状态
        /// </summary>
        /// <param name="msg"></param>
        private void WriteStatus(string msg, UILogType logType = UILogType.Default)
        {

            ControlUtil.ExcuteAction(this, () =>
            {
                switch (logType)
                {
                    case UILogType.Success:
                        tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("Green"));
                        break;
                    case UILogType.Error:
                        tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("Red"));
                        break;
                    case UILogType.Warning:
                        tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFAE00"));
                        break;
                    default:
                        tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF34495E"));
                        break;
                }
                tbStatus.Text = msg;
            });
        }

        /// <summary>
        /// 设置FTP连接操作的控件可用与不可用操作 
        /// </summary>
        /// <param name="isSuccess"></param>
        private void SetFTPControlStatus(bool isSuccess = false)
        {
            if (isSuccess)
            {
                txtAddress.IsEnabled = false;
                txtName.IsEnabled = false;
                txtPassword.IsEnabled = false;
                txtPassword.IsEnabled = false;
                txtPort.IsEnabled = false;
                txtDirectory.IsEnabled = false;
                btnConnectOpen.Visibility = Visibility.Collapsed;
                btnConnectClose.Visibility = Visibility.Visible;
                btnFTPServerView.Visibility = Visibility.Visible;
            }
            else
            {
                txtAddress.IsEnabled = true;
                txtName.IsEnabled = true;
                txtPassword.IsEnabled = true;
                txtPassword.IsEnabled = true;
                txtPort.IsEnabled = true;
                txtDirectory.IsEnabled = true;
                btnConnectOpen.Visibility = Visibility.Visible;
                btnConnectClose.Visibility = Visibility.Collapsed;
                btnFTPServerView.Visibility = Visibility.Collapsed;
            }
            ShowRegisterButton(licenseFileModel.IsTrial);
        }

        /// <summary>
        /// 获取服务器文件存储路径
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="fileName"></param>
        private string GetServerFileFullPath(TaskEntity taskEntity, TaskSettingModel setting, FileInfo fileModel, string selectFolderPath)
        {
            if (fileModel == null)
                return "";

            if (ftpUtil == null || (ftpUtil != null && ftpUtil.ClientModel == null))
                return fileModel.Name;

            var serverPathValue = setting.RootPath;
            if (!serverPathValue.EndsWith("/"))
            {
                serverPathValue = serverPathValue + "/";
            }

            if (!string.IsNullOrWhiteSpace(selectFolderPath))
            {
                var tempPath = fileModel.FullName.Replace(selectFolderPath, "").Replace("\\", "/");
                return string.Format("{0}{1}", serverPathValue, tempPath).Replace("//", "/");
            }
            return string.Format("{0}{1}", serverPathValue, fileModel.Name);
        }

        /// <summary>
        /// 替换标签
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="taskEntity"></param>
        /// <param name="taskFileEntity"></param>
        /// <returns></returns>
        private string ReplaceSqlTag(string sql, TaskEntity taskEntity, TaskFileEntity taskFileEntity)
        {
            if (VerifyHelper.IsEmpty(sql))
                return sql;
            //{FileFullPath}->文路径径，{FileSize}->文件大小，{ServerFileFullPath} 文件后缀 {Extension},上传时间：{UpDateTime}
            var currentDateTimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return sql.Replace("{FileFullPath}", taskFileEntity.FilePath).Replace("{filefullpath}", taskFileEntity.FilePath).Replace("{FILEFULLPATH}", taskFileEntity.FilePath)
                .Replace("{ServerFileFullPath}", taskFileEntity.ServerFullPath).Replace("{serverfilefullpath}", taskFileEntity.FilePath).Replace("{SERVERFILEFULLPATH}", taskFileEntity.FilePath)
                .Replace("{FileSize}", taskFileEntity.FileSize.ToString()).Replace("{filesize}", taskFileEntity.FileSize.ToString()).Replace("{FILESIZE}", taskFileEntity.FileSize.ToString())
                .Replace("{Extension}", taskFileEntity.FileExtension).Replace("{extension}", taskFileEntity.FileExtension).Replace("{EXTENSION}", taskFileEntity.FileExtension)
                .Replace("{UpDateTime}", currentDateTimeStr).Replace("{updatetime}", currentDateTimeStr).Replace("{UPDATETIME}", currentDateTimeStr);
        }

        /// <summary>
        /// 获取文件MD5
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static string GetFileMD5(string fileName)
        {
            try
            {
                FileStream file = new FileStream(fileName, FileMode.Open);
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(file);
                file.Close();

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                throw new Exception("GetFileMD5() fail, error:" + ex.Message);
            }
        }

        /// <summary>
        /// 上传信息参数
        /// </summary>
        /// <param name="param"></param>
        /// <param name="returnJosnObj"></param>
        /// <returns></returns>
        private string GetHttpPostByRequestParams(TaskEntity taskEntity, TaskFileEntity taskFileEntity, bool isAfterParam = false, JObject returnJosnObj = null)
        {
            JObject post = new JObject();
            if (taskFileEntity != null && taskEntity != null)
            {
                List<RequestParamModel> param = new List<RequestParamModel>();
                if (!isAfterParam)
                {
                    var berforeSetting = JsonHelper.Deserialize<RequestModel>(taskEntity.BeforeJSON);
                    param = berforeSetting.Params;
                }
                else
                {
                    var afterSetting = JsonHelper.Deserialize<RequestModel>(taskEntity.AfterJSON);
                    param = afterSetting.Params;
                }

                foreach (var item in param)
                {
                    if (item.Name.ToLower() == "id")
                    {
                        post["Id"] = taskFileEntity.Id;
                    }
                    if (item.Name.ToLower() == "filefullpath")
                    {
                        post["FileFullPath"] = taskFileEntity.FilePath;
                    }
                    if (item.Name.ToLower() == "filemd5")
                    {

                        post["FileMD5"] = GetFileMD5(taskFileEntity.FilePath);
                    }
                    if (item.Name.ToLower() == "serverfilefullpath")
                    {
                        post["ServerFileFullPath"] = taskFileEntity.ServerFullPath;
                    }
                    if (item.Name.ToLower() == "filesize")
                    {
                        post["FileSize"] = taskFileEntity.FileSize;
                    }
                    if (item.Name.ToLower() == "extension")
                    {
                        post["Extension"] = taskFileEntity.FileExtension;
                    }
                    if (item.Name.ToLower() == "updatetime")
                    {
                        post["UpDateTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    if (item.PType.ToLower() == "value")
                    {
                        post[item.Name] = item.DefaultValue;
                    }
                    if (item.PType.ToLower() == "return" && returnJosnObj != null)
                    {
                        post[item.Name] = returnJosnObj.GetJTokenValue(item.Name);
                    }
                }
            }
            return JsonHelper.Serialize(post);
        }

        /// <summary>
        /// Http请求并返回HttpResult.Html String
        /// </summary>
        /// <param name="url"></param>
        /// <param name="post"></param>
        /// <param name="method"></param>
        /// <param name="encoding"></param>
        /// <param name="contentType"></param>
        /// <param name="heads"></param>
        /// <returns></returns>
        private string GetHttp(string url, string post = null, string method = null, Encoding encoding = null, string contentType = null, WebHeaderCollection heads = null)
        {

            HttpItem item = new HttpItem();

            #region 默认值设置

            if (VerifyHelper.IsEmpty(encoding))
            {
                encoding = Encoding.UTF8;
            }

            if (VerifyHelper.IsEmpty(method))
            {
                method = "POST";
                if (VerifyHelper.IsEmpty(post))
                {
                    method = "GET";
                }
            }

            if (VerifyHelper.IsEmpty(contentType))
            {
                contentType = "application/json";
            }

            //不指定编辑可能导致接收不到数据(中文)
            if (VerifyHelper.IsEqualString(method, "post"))
            {
                item.PostEncoding = encoding;
            }

            #endregion

            item.Postdata = post;
            item.URL = url;
            item.Encoding = encoding;
            item.ContentType = contentType;
            item.Method = method;
            item.Header = heads;

            return GetHttp(item).Html;
        }

        /// <summary>
        /// Http请求并返回HttpResult
        /// </summary>
        private HttpResult GetHttp(HttpItem item)
        {
            if (VerifyHelper.IsNull(item))
            {
                throw new Exception("Http Error");
            }

            var httpResult = new HttpHelper().GetHtml(item);

            #region 请求结果处理

            if (VerifyHelper.IsNull(httpResult))
            {
                throw new Exception("Http Error");
            }

            if (VerifyHelper.IsEmpty(httpResult.Html))
            {
                throw new Exception("Http Error");
            }
            if (httpResult.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Http Error");
            }
            #endregion

            return httpResult;
        }


        #endregion

        #region 辅助方法-配置校验

        private void VerifyConfig()
        {
            ConfigModel config = null;

            try
            {
                config = AppHelper.GetConfig();
                if (config == null)
                {
                    if (MessageBox.Show("软件配置信息错误", "系统错误", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }
            }
            catch
            {
                if (MessageBox.Show("软件配置信息错误", "系统错误", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }
        }

        #endregion

        #region 辅助方法-授权文件

        private void Loading()
        {
            #region 控件初始

            btnFTPServerView.Visibility = Visibility.Collapsed;
            tabDatabase.Visibility = Visibility.Collapsed;
            tabApiBefore.Visibility = Visibility.Collapsed;
            tabApiAfter.Visibility = Visibility.Collapsed;

            #endregion

            #region 授权信息

            string url = AppHelper.GetConfig().WebUrl;

            try
            {
                var licPath = string.Format("{0}/data/license.key", AppDomain.CurrentDomain.BaseDirectory).ToLower();
                licenseFileModel = AppHelper.LicenseFileGet(licPath);
                licenseStatModel = AppHelper.LicenseStatGet(licenseFileModel);

                //信息校验
                try
                {
                    AppHelper.LicenseVerify(licenseFileModel, licenseStatModel);
                }
                catch (LicenseException ex)
                {
                    if (MessageBox.Show(string.Format("{0}(官方网站：{1})", ExceptionHelper.GetMessage(ex), url), "提示信息", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", url);
                        btnTaskRunning.Visibility = Visibility.Collapsed;
                    }
                }
                
                //基本版设置
                if (licenseFileModel.Edition == EnumEditionType.Basic.ToString())
                {
                    if (!licenseFileModel.IsTrial)
                    {
                        this.LogoPath = "pack://application:,,,/Image/acp-base-logo.png";
                    }
                    else
                    {
                        this.LogoPath = "pack://application:,,,/Image/acp-base-try-logo.png";
                        this.Descript = GetTryDescript(licenseFileModel, licenseStatModel);
                    }

                    tabDatabase.Visibility = Visibility.Visible;
                }

                //专业版设置
                if (licenseFileModel.Edition == EnumEditionType.Professional.ToString())
                {
                    if (!licenseFileModel.IsTrial)
                    {
                        this.LogoPath = "pack://application:,,,/Image/acp-pro-logo.png";
                    }
                    else
                    {
                        this.LogoPath = "pack://application:,,,/Image/acp-pro-try-logo.png";
                        this.Descript = GetTryDescript(licenseFileModel, licenseStatModel);
                    }

                    //tabDatabase.Visibility = Visibility.Visible;
                    tabApiBefore.Visibility = Visibility.Visible;
                    tabApiAfter.Visibility = Visibility.Visible;
                }

            }
            catch (Exception ex)
            {
                if (MessageBox.Show(string.Format("{0}(官方网站：{1})", ExceptionHelper.GetMessage(ex), url), "提示信息", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    System.Diagnostics.Process.Start("explorer.exe", url);
                    Application.Current.Shutdown();
                    return;
                }
            }

            #endregion

            //注册按钮
            ShowRegisterButton(licenseFileModel.IsTrial);
        }

        private void ShowRegisterButton(bool isTrial)
        {
            if (!isTrial)
            {
                //注册软件
                if (licenseFileModel.Edition == EnumEditionType.Basic.ToString())
                {
                    this.btnRegister.Content = "基础正式版";
                }
                if (licenseFileModel.Edition == EnumEditionType.Professional.ToString())
                {
                    this.btnRegister.Content = "高级正式版";
                }
                this.btnRegister.Style = this.FindResource("Button-Info") as Style;
                this.btnRegister.ApplyTemplate();

                if (ftpUtil != null && ftpUtil.IsConnection)
                {
                    this.btnRegister.Visibility = Visibility.Collapsed;     //隐藏授权信息
                    this.btnFTPServerView.Visibility = Visibility.Visible;  //显示目录浏览
                }
                else
                {
                    this.btnRegister.Visibility = Visibility.Visible;     //隐藏授权信息
                    this.btnFTPServerView.Visibility = Visibility.Collapsed;  //显示目录浏览
                }
            }
            else
            {
                this.btnRegister.Content = "试用软件注册";
                this.btnRegister.Style = this.FindResource("Button-Danger") as Style;
                this.btnRegister.ApplyTemplate();
                this.btnRegister.Visibility = Visibility.Visible;

                this.btnFTPServerView.Visibility = Visibility.Collapsed;
            }
        }

        private string GetTryDescript(LicenseFileModel fileModel, LicenseStatModel licenseStatModel)
        {
            if (licenseFileModel.IsTrial)
            {
                this.IsDescriptVisible = true;
            }
            else
            {
                this.IsDescriptVisible = false;
                return "";
            }

            //"基础试用版 单个文件大小100kb"
            StringBuilder builder = new StringBuilder();

            if (fileModel.Edition == EnumEditionType.Basic.ToString())
            {
                builder.Append("基础试用版限制：");
            }
            if (fileModel.Edition == EnumEditionType.Professional.ToString())
            {
                builder.Append("高级试用版限制：");
            }

            builder.Append("单个文件大小100kb ");

            //是否可继续试用(判断时间及次数)
            var maxCount = LongHelper.Get(fileModel.Times);
            if (maxCount > 0)
            {
                builder.AppendFormat("，试用次数 {0}/{1} ", licenseStatModel.Count, maxCount);
            }

            //可以通过http获取在线时间（防止更改时间）
            var curDate = DateTime.Now;
            var minDate = DateTimeHelper.Get("1900-01-01 00:00");

            //时间格式：字符串日期 201x-xx-xx（指定日期过期）
            var maxDate = DateTimeHelper.Get(fileModel.Date, defaultValue: minDate);
            if (maxDate > minDate)
            {
                builder.AppendFormat("，过期日期 {0} ", DateTimeHelper.GetEnd(maxDate));
            }

            //时间格式：字符串数字 2 (安装后2天过期)
            var maxDate2 = IntHelper.Get(fileModel.Date);
            if (maxDate2 > 0)
            {
                builder.AppendFormat("，过期日期 {0} ", DateTimeHelper.GetEnd(licenseStatModel.Create.AddDays(maxDate2)));
            }

            return builder.ToString();
        }

        #endregion

        #region 辅助方法-升级判断

        protected void LastVerision()
        {
            var config = AppHelper.GetConfig();
            try
            {
                string currentVerision = "";
                System.Diagnostics.FileVersionInfo myFileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Windows.Forms.Application.ExecutablePath);
                currentVerision = myFileVersion.FileVersion;

                if (!VerifyHelper.IsEmpty(config) &&
                    !VerifyHelper.IsEmpty(config.VersionUrl) &&
                    config.VersionUrl != "#" &&
                    (config.VersionUrl.ToLower().Contains("http://") || config.VersionUrl.ToLower().Contains("https://")))
                {
                    string url = string.Format("{0}{1}version={2}&code={3}&edition={4}", config.VersionUrl.ToLower(),
                        (config.VersionUrl.IndexOf("?") > -1 ? "&" : "?"), currentVerision, config.Businesser, licenseFileModel.Edition);

                    var result = GetHttp(url);
                    if (!VerifyHelper.IsEmpty(result))
                    {
                        var verModel = JsonHelper.Deserialize<VersionModel>(result);
                        if (verModel.CurrentVersion != verModel.LastVersion)
                        {
                            Update win = new Update(verModel);
                            win.Owner = this;
                            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            win.ShowDialog();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ControlUtil.ShowMsg(string.Format("版本校验/更新出错，为保证正常使用，请至官网下载最新版。({0})", config.WebUrl), "更新提示", MessageBoxButton.OK, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    System.Diagnostics.Process.Start("explorer.exe", config.WebUrl);
                    //Application.Current.Shutdown();
                    return;
                }
            }
        }

        #endregion

    }

}
