﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;

namespace LidarTestGUI
{
    public partial class Form1 : Form
    {
        public SerialPort LidarSerialPort;

        //报文帧头, 帧长度, 缓冲区
        private static readonly byte[] _frameHeader = new byte[] { 0x55, 0xAA };
        private static readonly int _frameLength = 60;
        private static List<byte> _buffer = new List<byte>();

        //速度图, 点云图
        private ScottPlot.Plottables.DataStreamer Streamer1;
        private static ScottPlot.WinForms.FormsPlot fpSpeed;
        private static ScottPlot.WinForms.FormsPlot fpPosition;

        private static CancellationTokenSource _cts = new CancellationTokenSource();

        //更新速度图和点云图绘制定时器
        private System.Windows.Forms.Timer UpdateSpeedPlotTimer = new System.Windows.Forms.Timer() { Interval = 50, Enabled = true };
        private System.Windows.Forms.Timer UpdatePosPlotTimer = new System.Windows.Forms.Timer() { Interval = 200, Enabled = true };

        //存储点云绘图坐标及角度
        private static List<double> positionX = new List<double>();
        private static List<double> positionY = new List<double>();
        private static List<double> lastAngle = new List<double>();

        //存储报文数据
        private static int[] MeasurePointDistance = new int[16];
        private static int[] MeasurePointQuality = new int[16];
        private static double[] MeasurePointAngle = new double[16];

        private static List<double> xs = new List<double>();
        private static List<double> ys = new List<double>();

        private static int RefreshPlotCounter = 0;
        public Form1()
        {
            InitializeComponent();

            fpSpeed = new ScottPlot.WinForms.FormsPlot() { Dock = DockStyle.Fill };
            this.groupBox1.Controls.Add(fpSpeed);
            fpSpeed.Interaction.Disable();
            Streamer1 = fpSpeed.Plot.Add.DataStreamer(1000);
            Streamer1.ViewScrollLeft();
            fpSpeed.Plot.Axes.ContinuouslyAutoscale = true;

            UpdateSpeedPlotTimer.Tick += (s, e) =>
            {
                if (Streamer1.HasNewData)
                    fpSpeed.Refresh();
            };

            fpPosition = new ScottPlot.WinForms.FormsPlot() { Dock = DockStyle.Fill };
            this.groupBox2.Controls.Add(fpPosition);
            fpPosition.Plot.Axes.SquareUnits();

            positionX.Add(0.0);
            positionY.Add(0.0);
            var abc = fpPosition.Plot.Add.ScatterPoints(positionX, positionY);
            fpPosition.Plot.Axes.SetLimitsX(-2000, 2000);
            fpPosition.Plot.Axes.SetLimitsY(-2000, 2000);

            //UpdatePosPlotTimer.Tick += (s, e) =>
            //{
                //fpPosition.Plot.Axes.AutoScale();
                //fpPosition.Refresh();
            //};

            comboBox_sp.DataSource = SerialPort.GetPortNames();
            comboBox_sp.SelectedIndex = 0;

            LidarSerialPort = new SerialPort();

            LidarSerialPort.PortName = comboBox_sp.SelectedItem.ToString();
            LidarSerialPort.BaudRate = 230400;

        }
        private void LidarSerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            while (_cts.Token.IsCancellationRequested)
            {
                return;
            }
            try
            {
                while (sp.BytesToRead > 0)
                {
                    _buffer.Add((byte)sp.ReadByte());
                }
                ProcessBuffer();
            }
            catch (Exception ex) 
            {

            }
        }
        private static int FindFrameStart()
        {
            for (int i = 0; i <= _buffer.Count - _frameHeader.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < _frameHeader.Length; j++)
                {
                    if (_buffer[i + j] != _frameHeader[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }
            return -1;
        }
        private void ProcessBuffer()
        {
            while (!_cts.Token.IsCancellationRequested && true) // 持续处理_buffer中的数据
            {
                int frameStartIndex = FindFrameStart();
                if (frameStartIndex < 0 || frameStartIndex + _frameLength > _buffer.Count)
                {
                    break;
                }

                byte[] frame = _buffer.GetRange(frameStartIndex, _frameLength).ToArray();


                ProcessFrame(frame);


                _buffer.RemoveRange(0, frameStartIndex + _frameLength);
            }
        }
        public void ProcessFrame(byte[] frameData)
        {
            //Trace.WriteLine(BitConverter.ToString(frameData));
            //Trace.WriteLine(speed);
            //速度, 开始结束角度
            var speed = (frameData[5] << 8 | frameData[4]) / 64.0;
            var startAngle = (frameData[7] << 8 | frameData[6]) / 64.0 - 640.0;
            var endAngle = (frameData[57] << 8 | frameData[56]) / 64.0 - 640.0;

            //角度间距
            //跨0度的点
            if (endAngle < startAngle)
            {
                endAngle += 360.0;
            }
            var spaceAngle = (endAngle - startAngle) / 15.0;

            //删除旧的点
            if (lastAngle.Count > 0)
            {
                if (startAngle < endAngle)
                {
                    var temp = lastAngle.Where(o => o >= startAngle && o <= endAngle).ToList();
                    if (temp.Count > 0)
                    {
                        var left = lastAngle.IndexOf(temp[0]);
                        var count = temp.Count;
                        if (left != -1)
                        {
                            positionX.RemoveRange(left, count);
                            positionY.RemoveRange(left, count);
                            lastAngle.RemoveRange(left, count);
                        }
                    }

                }
                else
                {
                    //跨0度左侧
                    var temp1 = lastAngle.Where(o => o >= startAngle && o < 360).ToList();
                    if (temp1.Count > 0)
                    {
                        var left1 = lastAngle.IndexOf(temp1[0]);
                        var count1 = temp1.Count;
                        if (left1 != -1)
                        {
                            positionX.RemoveRange(left1, count1);
                            positionY.RemoveRange(left1, count1);
                            lastAngle.RemoveRange(left1, count1);
                        }
                    }

                    //跨0度右侧
                    var temp2 = lastAngle.Where(o => o >= 0 && o <= endAngle).ToList();
                    if (temp2.Count > 0)
                    {
                        var left2 = lastAngle.IndexOf(temp2[0]);
                        var count2 = temp2.Count;
                        if (left2 != -1)
                        {
                            positionX.RemoveRange(left2, count2);
                            positionY.RemoveRange(left2, count2);
                            lastAngle.RemoveRange(left2, count2);
                        }
                    }

                }
            }

            //16个测量点, 角度插值
            for (int index = 0; index < 16; index++)
            {
                MeasurePointDistance[index] = frameData[9 + 3 * index] << 8 | frameData[8 + 3 * index];
                MeasurePointQuality[index] = frameData[10 + 3 * index];
                MeasurePointAngle[index] = startAngle + spaceAngle * index;
                //跨0度的点
                if (MeasurePointAngle[index] > 360.0)
                {
                    MeasurePointAngle[index] -= 360.0;
                }
            }


            //更新速度曲线数据
            fpSpeed.BeginInvoke((MethodInvoker)delegate
            {
                Streamer1.Add(speed);
            });

            //更新点云数据
            xs.Clear();
            ys.Clear();
            for (int index = 0; index < 16; index++)
            {
                //quality意义不明, 临时过滤异常大的值
                if (MeasurePointDistance[index] < 15000)
                {
                    //记录新绘制的点
                    lastAngle.Add(MeasurePointAngle[index]);
                    //极坐标转直角坐标
                    var c = Complex.FromPolarCoordinates(MeasurePointDistance[index], -MeasurePointAngle[index] * Math.PI / 180);
                    xs.Add(c.Real);
                    ys.Add(c.Imaginary);
                }
            }
            positionX.AddRange(xs);
            positionY.AddRange(ys);
            RefreshPlotCounter += 1;
            if(RefreshPlotCounter > 35)
            {
                RefreshPlotCounter = 0;
                try
                {
                    fpPosition.Refresh();
                }
                catch
                {
                }    
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "打开")
            {
                _cts = new CancellationTokenSource();
                LidarSerialPort.Open();
                LidarSerialPort.DataReceived += LidarSerialPort_DataReceived;
                button1.Text = "关闭";
            }
            else
            {
                _cts.Cancel();
                LidarSerialPort.DataReceived -= LidarSerialPort_DataReceived;
                LidarSerialPort.Close();
                button1.Text = "打开";
            }
        }

        private void comboBox_sp_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (LidarSerialPort != null)
            {
                LidarSerialPort.PortName = comboBox_sp.SelectedItem.ToString();
            }
        }

        private void ToolStripMenuItem_about_Click(object sender, EventArgs e)
        {
            Form2 frm2 = new Form2();
            frm2.ShowDialog();
        }
    }
}