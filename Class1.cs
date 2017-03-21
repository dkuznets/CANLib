using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using System.IO;

using HANDLE = System.IntPtr;

#region переопределение типов
    using Byte = System.Byte;
    using SByte = System.SByte;
    using UInt16 = System.UInt16;
    using Int16 = System.Int16;
    using UInt32 = System.UInt32;
    using Int32 = System.Int32;
#endregion
#region переопределение типов
    using _u8 = System.Byte;
    using _s8 = System.SByte;
    using _u16 = System.UInt16;
    using _s16 = System.Int16;
    using _u32 = System.UInt32;
    using _s32 = System.Int32;
#endregion	

/// <summary>
/// 1.3.0.100  - Добавлено определение mID_REQVER = 0x20;
/// 1.3.0.99  - Добавлен fake-драйвер CAN для локальной отладки.
/// 1.2.1.96  - Добавлено определение для бродкаста .
/// 1.2.1.95  - Добавлена функция сброса.
/// 1.2.0.93  - Первая попытка переделать драйвер под Марафон.
/// 1.1.0.89  - Перенесены классы и дефайны из проектов.
/// 1.0.15.71 - Найден косяк в Recv.
/// 1.0.15.69 - Все равно глючит. Но работает. Найден косяк с Элкусом.
/// 1.0.14.65 - Заработало с Марафоном. Вроде как. Добавлен делегат и эвент для прогресса.
/// 1.0.14.63 - Заработало с Элкусом. Перестало работать с Марафоном. Сцуко.
/// 1.0.13.57 - Вроде как нормально заработало с Элкусом
/// </summary>
/// <param name="sender"></param>
/// <param name="e"></param>

    public delegate void MyDelegate(object sender, MyEventArgs e);

    public interface IUCANConverter
    {
        event MyDelegate ErrEvent;
        event MyDelegate Progress;
        string Info { get; set; }
        Byte Port { get; set; }
        Byte Speed { get; set; }
        Boolean Is_Open { get; set; }
        Boolean Is_Present { get; set; }
        String GetAPIVer { get; set; }
        Boolean Open();
        void Close();
        Boolean Send(ref canmsg_t msg);
        Boolean Send(ref canmsg_t msg, int timeout);
        Boolean Recv(ref canmsg_t msg, int timeout);
        Boolean RecvPack(ref Byte[] arr, ref int count, int timeout);
        Boolean SendCmd(ref canmsg_t msg, int timeout);
        void Recv_Enable();
        void Recv_Disable();
        void Clear_RX();
        int GetStatus();
        int VectorSize();
        void HWReset();
    }
    public class MyEventArgs : EventArgs
    {
        public MyEventArgs(string s)
        {
            Text = s;
        }
        public MyEventArgs(int s)
        {
            Val = s;
        }
        public String Text { get; private set; } // readonly    
        public int Val { get; private set; } // readonly    
    }

    #region ACANConverter
    public class ACANConverter : IUCANConverter
    {
        public event MyDelegate ErrEvent;
        public event MyDelegate Progress;

        public static canerrs_t errs = new canerrs_t();
        public static canwait_t cw = new canwait_t();
        public static canmsg_t frame = new canmsg_t();

        public static AdvCANIO Device = new AdvCANIO();
        public ACANConverter()
        {
            Port = 0;
            Speed = 0;
            Is_Open = false;
            Info = "";
        }
        public String Info
        {
            get;
            set;
        }
        public Byte Port
        {
            get;
            set;
        }
        public Byte Speed
        {
            get;
            set;
        }
        public Boolean Is_Open
        {
            get;
            set;
        }
        public Boolean Is_Present
        {
            get
            {
                if (Open())
                {
                    Close();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set
            {
            }
        }
        public String GetAPIVer
        {
            get
            {
                return "";
            }
            set { }
        }
        public Boolean Open()
        {
            int nRet = 0;
            AdvCan.CanStatusPar_t CanStatus = new AdvCan.CanStatusPar_t();
            try
            {
                nRet = Device.acCanOpen("can1", false, 0, 0);   //Open CAN port
            }
            catch (Exception)
            {
            }
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу открыть CAN"));
                return false;
            }
            try
            {
                nRet = Device.acEnterResetMode();   //Enter reset mode          
            }
            catch (Exception)
            {
            }
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу открыть CAN"));
                Device.acCanClose();
                return false;
            }

            Info = "PCI-1680U-BE (Advantech Corp.)";

            nRet = Device.acSetBaud(1000);                               //Set Baud Rate
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу установить скорость"));
                Device.acCanClose();
                return false;
            }

            nRet = Device.acSetAcceptanceFilterMode(AdvCan.PELICAN_SINGLE_FILTER);
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set acceptance filter mode!"));
                Device.acCanClose();
                return false;
            }

            nRet = Device.acSetAcceptanceFilterMask(0xFFFFFFFF);                        //Set acceptance mask
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set acceptance mask!"));
                Device.acCanClose();
                return false;
            }

            nRet = Device.acSetAcceptanceFilterCode(0xFFFFFFFF);                          //Set acceptance code
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Invalid acceptance code value!"));
                Device.acCanClose();
            }

            nRet = Device.acSetTimeOut(3000, 3000);      //Set timeout
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set Timeout!"));
                Device.acCanClose();
            }

            nRet = Device.acGetStatus(ref CanStatus);                       //Get status
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to get current status!"));
                Device.acCanClose();
            }

            Is_Open = true;

            return true;
        }
        public void Close()
        {
            Device.acCanClose();
            Is_Open = false;
        }
        public Boolean Send(ref canmsg_t msg)
        {
            int nRet = 0;
            if (!Is_Open)
                Open();
            nRet = Device.acSetSelfReception(false);                            //Set self reception
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set self reception!"));
                Device.acCanClose();
                return false;
            }

            nRet = Device.acEnterWorkMode();                                   //Enter work mdoe
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to restart operation!"));
                Device.acCanClose();
                return false;
            }

            AdvCan.canmsg_t[] msgWrite = new AdvCan.canmsg_t[1];                 //Package for write   
            uint pulNumberofWritten = 0;

            //Initialize msg
            msgWrite[0].flags = AdvCan.MSG_EXT;
            msgWrite[0].cob = 0;
            msgWrite[0].id = msg.id;
            msgWrite[0].length = msg.len;
            msgWrite[0].data = msg.data;
            //if (rtr)
            //{
            //    msgWrite[0].flags += AdvCan.MSG_RTR;
            //    msgWrite[0].length = 0;
            //}

            nRet = Device.acCanWrite(msgWrite, 1, ref pulNumberofWritten); //Send frames
            if (nRet == AdvCANIO.TIME_OUT)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Sending timeout!"));
                Device.acCanClose();
                return false;
            }
            else if (nRet == AdvCANIO.OPERATION_ERROR)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Sending error!"));
                Device.acCanClose();
                return false;
            }
            else
            {
                return true;
            }
        }
        public Boolean Send(ref canmsg_t msg, int timeout)
        {
            int nRet = 0;
            if (!Is_Open)
                Open();

            nRet = Device.acSetTimeOut(3000, (uint)timeout);      //Set timeout
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set Timeout!"));
                Device.acCanClose();
            }

            nRet = Device.acSetSelfReception(false);                            //Set self reception
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set self reception!"));
                Device.acCanClose();
                return false;
            }

            nRet = Device.acEnterWorkMode();                                   //Enter work mdoe
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to restart operation!"));
                Device.acCanClose();
                return false;
            }

            AdvCan.canmsg_t[] msgWrite = new AdvCan.canmsg_t[1];                 //Package for write   
            uint pulNumberofWritten = 0;

            //Initialize msg
            msgWrite[0].flags = AdvCan.MSG_EXT;
            msgWrite[0].cob = 0;
            msgWrite[0].id = msg.id;
            msgWrite[0].length = msg.len;
            msgWrite[0].data = msg.data;
            //if (rtr)
            //{
            //    msgWrite[0].flags += AdvCan.MSG_RTR;
            //    msgWrite[0].length = 0;
            //}

            nRet = Device.acCanWrite(msgWrite, 1, ref pulNumberofWritten); //Send frames
            if (nRet == AdvCANIO.TIME_OUT)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Sending timeout!"));
                Device.acCanClose();
                return false;
            }
            else if (nRet == AdvCANIO.OPERATION_ERROR)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Sending error!"));
                Device.acCanClose();
                return false;
            }
            else
            {
                return true;
            }
        }
        public Boolean Recv(ref canmsg_t msg, int timeout)
        {
            int nRet = 0;
            canwait_t cw = new canwait_t();
            uint pulNumberofRead = 0;
            uint ReceiveIndex = 0;

            AdvCan.canmsg_t[] msgRead = new AdvCan.canmsg_t[1];
            msgRead[0].data = new byte[8];

            if (!Is_Open)
                Open();

            nRet = Device.acSetTimeOut((uint)timeout, 3000);      //Set timeout
            if (nRet < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Failed to set Timeout!"));
                Device.acCanClose();
            }
            try
            {
                nRet = Device.acCanRead(msgRead, 1, ref pulNumberofRead); //Receiving frames
            }
            catch (Exception)
            {
            }
            if (nRet == AdvCANIO.TIME_OUT)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Receiving timeout!"));
                Device.acCanClose();
                return false;
            }
            else if (nRet == AdvCANIO.OPERATION_ERROR)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Package error!"));
                Device.acCanClose();
                return false;
            }
            else
            {
                if (msgRead[0].id == AdvCan.ERRORID)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Package error!"));
                }
                else
                {
                    if ((msgRead[0].flags & AdvCan.MSG_RTR) > 0)
                    {
                        if (ErrEvent != null)
                            ErrEvent(this, new MyEventArgs("RTR package!"));
                    }
                    else
                    {
                        msg.data = msgRead[0].data;
                    }
                }
                return true;
            }

        }
        public Boolean RecvPack(ref Byte[] arr, ref int count, int timeout)
        {
            return true;
        }
        public Boolean SendCmd(ref canmsg_t msg, int timeout)
        {
            if (!Send(ref msg))
                return false;
            if (!Recv(ref msg, timeout))
                return false;
            return true;
        }
        public int GetStatus()
        {
            int result = 0;
            return result;
        }
        public void Recv_Enable()
        {
            Trace.WriteLine("Adv Recv Enable");
        }
        public void Recv_Disable()
        {
            Trace.WriteLine("Adv Recv Enable");
        }
        public int VectorSize()
        {
            return 0;
        }
        public void Clear_RX()
        {
            Trace.WriteLine("Cleared: ");// + CAN200_ClearBuf(hCan, port).ToString());
        }
        public void HWReset()
        {
            Trace.WriteLine("Hardware reset");
        }

        ~ACANConverter()
        {
            if (Is_Open)
                Close();
        }
    }

    #region class AdvCANIO
    public class AdvCANIO
    {

        private IntPtr hDevice;                                                           //Device handle
        private IntPtr orgWriteBuf = IntPtr.Zero;                                         //Unmanaged buffer for write
        private IntPtr orgReadBuf = IntPtr.Zero;                                          //Unmanaged buffer for read
        private IntPtr lpCommandBuffer = IntPtr.Zero;                                     //Unmanaged buffer Command 
        private IntPtr lpConfigBuffer = IntPtr.Zero;                                      //Unmanaged buffer Config
        private IntPtr lpStatusBuffer = IntPtr.Zero;                                      //Unmanaged buffer Status
        private AdvCan.Command_par_t Command = new AdvCan.Command_par_t();                //Managed buffer for Command
        private AdvCan.Config_par_t Config = new AdvCan.Config_par_t();                   //Managed buffer for Cofig
        private int OutLen;                                                               //Out data length for DeviceIoControl
        private int OS_TYPE = System.IntPtr.Size;                                         //For judge x86 or x64 
        private uint EventCode = 0;                                                       //Event code for WaitCommEvent
        private IntPtr lpEventCode = IntPtr.Zero;                                         //Unmanaged buffer for Event code
        private IntPtr INVALID_HANDLE_VALUE = IntPtr.Zero;                                //Invalid handle
        public const int SUCCESS = 0;                                                     //Status definition : success
        public const int OPERATION_ERROR = -1;                                            //Status definition : device error or parameter error
        public const int TIME_OUT = -2;                                                   //Status definition : time out
        private uint MaxReadMsgNumber;                                                    //Max number of message in unmanaged buffer for read 
        private uint MaxWriteMsgNumber;                                                   //Max number of message in unmanaged buffer for write

        // Fields
        private Win32Events events;
        private Win32Ovrlap ioctlOvr;
        private ManualResetEvent ioctlEvent;
        private Win32Ovrlap txOvr;
        private ManualResetEvent writeEvent;
        private Win32Ovrlap rxOvr;
        private Win32Ovrlap eventOvr;
        AutoResetEvent readEvent;

        public AdvCANIO()
        {

            hDevice = INVALID_HANDLE_VALUE;

            lpCommandBuffer = Marshal.AllocHGlobal(AdvCan.CAN_COMMAND_LENGTH);
            lpConfigBuffer = Marshal.AllocHGlobal(AdvCan.CAN_CONFIG_LENGTH);
            lpStatusBuffer = Marshal.AllocHGlobal(AdvCan.CAN_CANSTATUS_LENGTH);
            lpEventCode = Marshal.AllocHGlobal(Marshal.SizeOf(EventCode));
            Marshal.StructureToPtr(EventCode, lpEventCode, true);

            this.ioctlEvent = new ManualResetEvent(false);
            this.ioctlOvr = new Win32Ovrlap(this.ioctlEvent.SafeWaitHandle.DangerousGetHandle());

            this.writeEvent = new ManualResetEvent(false);
            this.txOvr = new Win32Ovrlap(this.writeEvent.SafeWaitHandle.DangerousGetHandle());

            readEvent = new AutoResetEvent(false);
            this.rxOvr = new Win32Ovrlap(readEvent.SafeWaitHandle.DangerousGetHandle());

            this.eventOvr = new Win32Ovrlap(readEvent.SafeWaitHandle.DangerousGetHandle());
            this.events = new Win32Events(this.eventOvr.memPtr);
        }

        ~AdvCANIO()
        {
            if (hDevice != INVALID_HANDLE_VALUE)
            {
                AdvCan.CloseHandle(hDevice);
                Thread.Sleep(100);

                Marshal.FreeHGlobal(lpCommandBuffer);
                Marshal.FreeHGlobal(lpConfigBuffer);
                Marshal.FreeHGlobal(lpStatusBuffer);
                Marshal.FreeHGlobal(lpEventCode);
                Marshal.FreeHGlobal(orgWriteBuf);
                Marshal.FreeHGlobal(orgReadBuf);
                this.ioctlEvent = null;
                this.ioctlOvr = null;
                this.writeEvent = null;
                this.txOvr = null;
                this.eventOvr = null;
                this.events = null;
                hDevice = INVALID_HANDLE_VALUE;
            }

        }
        /*****************************************************************************
        *    acCanOpen
        *    Purpose:
        *    open can port by name 
        *    Arguments:
        *        PortName                - port name
        *        synchronization         - true, synchronization ; false, asynchronous
        *        MsgNumberOfReadBuffer   - message number of read intptr
        *        MsgNumberOfWriteBuffer  - message number of write intptr
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *****************************************************************************/
        public int acCanOpen(string CanPortName, bool synchronization, uint MsgNumberOfReadBuffer, uint MsgNumberOfWriteBuffer)
        {
            CanPortName = "\\\\.\\" + CanPortName;
            if (!synchronization)
                hDevice = AdvCan.CreateFile(CanPortName, AdvCan.GENERIC_READ + AdvCan.GENERIC_WRITE, 0, IntPtr.Zero, AdvCan.OPEN_EXISTING, AdvCan.FILE_ATTRIBUTE_NORMAL + AdvCan.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            else
                hDevice = AdvCan.CreateFile(CanPortName, AdvCan.GENERIC_READ + AdvCan.GENERIC_WRITE, 0, IntPtr.Zero, AdvCan.OPEN_EXISTING, AdvCan.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (hDevice.ToInt32() == -1)
            {
                hDevice = INVALID_HANDLE_VALUE;
                return OPERATION_ERROR;
            }
            if (hDevice != INVALID_HANDLE_VALUE)
            {
                MaxReadMsgNumber = MsgNumberOfReadBuffer;
                MaxWriteMsgNumber = MsgNumberOfWriteBuffer;
                orgReadBuf = Marshal.AllocHGlobal((int)(AdvCan.CAN_MSG_LENGTH * 10000));
                orgWriteBuf = Marshal.AllocHGlobal((int)(AdvCan.CAN_MSG_LENGTH * 10000));
                return SUCCESS;
            }
            else
                return OPERATION_ERROR;
        }
        /*****************************************************************************
        *    acCanClose
        *    Purpose:
        *        Close can port 
        *    Arguments:
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *****************************************************************************/
        public int acCanClose()
        {
            if (hDevice != INVALID_HANDLE_VALUE)
            {
                AdvCan.CloseHandle(hDevice);
                Thread.Sleep(100);
                Marshal.FreeHGlobal(orgWriteBuf);
                Marshal.FreeHGlobal(orgReadBuf);
                hDevice = INVALID_HANDLE_VALUE;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *    acEnterResetMode
        *    Purpose:
        *        Enter reset mode.
        *    Arguments:
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *****************************************************************************/
        public int acEnterResetMode()
        {
            bool flag;
            Command.cmd = AdvCan.CMD_STOP;
            Marshal.StructureToPtr(Command, lpCommandBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_COMMAND, lpCommandBuffer, AdvCan.CAN_COMMAND_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *    acEnterWorkMode
        *    Purpose:
        *        Enter work mode 
        *    Arguments:
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *****************************************************************************/
        public int acEnterWorkMode()
        {
            bool flag;
            Command.cmd = AdvCan.CMD_START;
            Marshal.StructureToPtr(Command, lpCommandBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_COMMAND, lpCommandBuffer, AdvCan.CAN_COMMAND_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acClearRxFifo
        *
        *    Purpose:
        *        Clear can port receive buffer
        *		
        *
        *    Arguments:
        *
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acClearRxFifo()
        {
            bool flag = false;
            Command.cmd = AdvCan.CMD_CLEARBUFFERS;
            Marshal.StructureToPtr(Command, lpCommandBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_COMMAND, lpCommandBuffer, AdvCan.CAN_COMMAND_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetBaud
        *
        *    Purpose:
        *	     Set baudrate of the CAN Controller.The two modes of configuring
        *     baud rate are custom mode and standard mode.
        *     -   Custom mode
        *         If Baud Rate value is user defined, driver will write the first 8
        *         bit of low 16 bit in BTR0 of SJA1000.
        *         The lower order 8 bit of low 16 bit will be written in BTR1 of SJA1000.
        *     -   Standard mode
        *         Target value     BTR0      BTR1      Setting value 
        *           10K            0x31      0x1c      10 
        *           20K            0x18      0x1c      20 
        *           50K            0x09      0x1c      50 
        *          100K            0x04      0x1c      100 
        *          125K            0x03      0x1c      125 
        *          250K            0x01      0x1c      250 
        *          500K            0x00      0x1c      500 
        *          800K            0x00      0x16      800 
        *         1000K            0x00      0x14      1000 
        *		
        *
        *    Arguments:
        *        BaudRateValue     - baudrate will be set
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetBaud(uint BaudRateValue)
        {
            bool flag;
            Config.target = AdvCan.CONF_TIMING;
            Config.val1 = BaudRateValue;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetBaudRegister
        *
        *    Purpose:
        *        Configures baud rate by custom mode.
        *		
        *
        *    Arguments:
        *        Btr0           - BTR0 register value.
        *        Btr1           - BTR1 register value.
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetBaudRegister(Byte Btr0, Byte Btr1)
        {
            uint BaudRateValue = (uint)(Btr0 * 256 + Btr1);
            return acSetBaud(BaudRateValue);
        }
        /*****************************************************************************
        *
        *    acSetTimeOut
        *
        *    Purpose:
        *        Set timeout for read and write  
        *		
        *
        *    Arguments:
        *        ReadTimeOutValue                   - ms
        *        WriteTimeOutValue                  - ms
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetTimeOut(uint ReadTimeOutValue, uint WriteTimeOutValue)
        {
            bool flag;
            Config.target = AdvCan.CONF_TIMEOUT;
            Config.val1 = WriteTimeOutValue;
            Config.val2 = ReadTimeOutValue;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetSelfReception
        *
        *    Purpose:
        *        Set support for self reception 
        *		
        *
        *    Arguments:
        *        SelfFlag      - true, open self reception; false, close self reception
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetSelfReception(bool SelfFlag)
        {
            bool flag;
            Config.target = AdvCan.CONF_SELF_RECEPTION;
            if (SelfFlag)
                Config.val1 = 1;
            else
                Config.val1 = 0;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetListenOnlyMode
        *
        *    Purpose:
        *        Set listen only mode of the CAN Controller
        *		
        *
        *    Arguments:
        *        ListenOnly        - true, open only listen mode; false, close only listen mode
        *    Returns:
        *        =0 succeeded; or <0 Failed 
        *
        *****************************************************************************/
        public int acSetListenOnlyMode(bool ListenOnly)
        {
            bool flag;
            Config.target = AdvCan.CONF_LISTEN_ONLY_MODE;
            if (ListenOnly)
                Config.val1 = 1;
            else
                Config.val1 = 0;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetAcceptanceFilterMode
        *
        *    Purpose:
        *        Set acceptance filter mode of the CAN Controller
        *		
        *
        *    Arguments:
        *        FilterMode     - PELICAN_SINGLE_FILTER, single filter mode; PELICAN_DUAL_FILTER, dule filter mode
        *    Returns:
        *        =0 succeeded; or <0 Failed 
        *
        *****************************************************************************/
        public int acSetAcceptanceFilterMode(uint FilterMode)
        {
            bool flag = false;
            Config.target = AdvCan.CONF_ACC_FILTER;
            Config.val1 = FilterMode;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetAcceptanceFilterMask
        *
        *    Purpose:
        *        Set acceptance filter mask of the CAN Controller
        *		
        *
        *    Arguments:
        *        Mask              - acceptance filter mask
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetAcceptanceFilterMask(uint Mask)
        {
            bool flag = false;
            Config.target = AdvCan.CONF_ACCM;
            Config.val1 = Mask;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetAcceptanceFilterCode
        *
        *    Purpose:
        *        Set acceptance filter code of the CAN Controller
        *		
        *
        *    Arguments:
        *        Code        - acceptance filter code
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetAcceptanceFilterCode(uint Code)
        {
            bool flag = false;
            Config.target = AdvCan.CONF_ACCC;
            Config.val1 = Code;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acSetAcceptanceFilter
        *
        *    Purpose:
        *        Set acceptance filter code and mask of the CAN Controller 
        *		
        *
        *    Arguments:
        *        Mask              - acceptance filter mask
        *        Code              - acceptance filter code
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acSetAcceptanceFilter(uint Mask, uint Code)
        {
            bool flag = false;
            Config.target = AdvCan.CONF_ACC;
            Config.val1 = Mask;
            Config.val2 = Code;
            Marshal.StructureToPtr(Config, lpConfigBuffer, true);
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_CONFIG, lpConfigBuffer, AdvCan.CAN_CONFIG_LENGTH, IntPtr.Zero, 0, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acGetStatus
        *
        *    Purpose:
        *        Get the current status of the driver and the CAN Controller
        *		
        *
        *    Arguments:
        *        Status    - status buffer
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acGetStatus(ref AdvCan.CanStatusPar_t Status)
        {
            bool flag = false;
            flag = AdvCan.DeviceIoControl(hDevice, AdvCan.CAN_IOCTL_STATUS, IntPtr.Zero, 0, lpStatusBuffer, AdvCan.CAN_CANSTATUS_LENGTH, ref OutLen, this.ioctlOvr.memPtr);
            if (!flag)
            {
                return OPERATION_ERROR;
            }
            Status = (AdvCan.CanStatusPar_t)(Marshal.PtrToStructure(lpStatusBuffer, typeof(AdvCan.CanStatusPar_t)));
            return SUCCESS;
        }
        /*****************************************************************************
        *
        *    acCanWrite
        *
        *    Purpose:
        *        Write can msg
        *		
        *
        *    Arguments:
        *        msgWrite              - managed buffer for write
        *        nWriteCount           - msg number for write
        *        pulNumberofWritten    - real msgs have written
        *
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acCanWrite(AdvCan.canmsg_t[] msgWrite, uint nWriteCount, ref uint pulNumberofWritten)
        {
            bool flag;
            int nRet;
            uint dwErr;
            if (nWriteCount > MaxWriteMsgNumber)
                nWriteCount = MaxWriteMsgNumber;
            pulNumberofWritten = 0;
            flag = AdvCan.WriteFile(hDevice, msgWrite, nWriteCount, out pulNumberofWritten, this.txOvr.memPtr);
            if (flag)
            {
                if (nWriteCount > pulNumberofWritten)
                    nRet = TIME_OUT;                          //Sending data timeout
                else
                    nRet = SUCCESS;                               //Sending data ok
            }
            else
            {
                dwErr = (uint)Marshal.GetLastWin32Error();
                if (dwErr == AdvCan.ERROR_IO_PENDING)
                {
                    if (AdvCan.GetOverlappedResult(hDevice, this.txOvr.memPtr, out pulNumberofWritten, true))
                    {
                        if (nWriteCount > pulNumberofWritten)
                            nRet = TIME_OUT;                    //Sending data timeout
                        else
                            nRet = SUCCESS;                         //Sending data ok
                    }
                    else
                        nRet = OPERATION_ERROR;                         //Sending data error
                }
                else
                    nRet = OPERATION_ERROR;                            //Sending data error
            }
            return nRet;
        }
        /*****************************************************************************
        *
        *    acCanRead
        *
        *    Purpose:
        *        Read can message.
        *		
        *
        *    Arguments:
        *        msgRead           - managed buffer for read
        *        nReadCount        - msg number that unmanaged buffer can preserve
        *        pulNumberofRead   - real msgs have read
        *		
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acCanRead(AdvCan.canmsg_t[] msgRead, uint nReadCount, ref uint pulNumberofRead)
        {
            bool flag;
            int nRet;
            uint i;
            if (nReadCount > MaxReadMsgNumber)
                nReadCount = MaxReadMsgNumber;
            pulNumberofRead = 0;
            flag = AdvCan.ReadFile(hDevice, orgReadBuf, nReadCount, out pulNumberofRead, this.rxOvr.memPtr);
            if (flag)
            {
                if (pulNumberofRead == 0)
                {
                    nRet = TIME_OUT;
                }
                else
                {
                    for (i = 0; i < pulNumberofRead; i++)
                    {
                        if (OS_TYPE == 8)
                            msgRead[i] = (AdvCan.canmsg_t)(Marshal.PtrToStructure(new IntPtr(orgReadBuf.ToInt64() + AdvCan.CAN_MSG_LENGTH * i), typeof(AdvCan.canmsg_t)));
                        else
                            msgRead[i] = (AdvCan.canmsg_t)(Marshal.PtrToStructure(new IntPtr(orgReadBuf.ToInt32() + AdvCan.CAN_MSG_LENGTH * i), typeof(AdvCan.canmsg_t)));

                    }
                    nRet = SUCCESS;
                }
            }
            else
            {
                if (AdvCan.GetOverlappedResult(hDevice, this.rxOvr.memPtr, out pulNumberofRead, true))
                {
                    if (pulNumberofRead == 0)
                    {
                        nRet = TIME_OUT;                               //Package receiving timeout
                    }
                    else
                    {
                        for (i = 0; i < pulNumberofRead; i++)
                        {
                            if (OS_TYPE == 8)
                                msgRead[i] = (AdvCan.canmsg_t)(Marshal.PtrToStructure(new IntPtr(orgReadBuf.ToInt64() + AdvCan.CAN_MSG_LENGTH * i), typeof(AdvCan.canmsg_t)));
                            else
                                msgRead[i] = (AdvCan.canmsg_t)(Marshal.PtrToStructure(new IntPtr(orgReadBuf.ToInt32() + AdvCan.CAN_MSG_LENGTH * i), typeof(AdvCan.canmsg_t)));
                        }
                        nRet = SUCCESS;
                    }
                }
                else
                    nRet = OPERATION_ERROR;                                    //Package receiving error
            }
            return nRet;
        }
        /*****************************************************************************
        *
        *    acClearCommError
        *
        *    Purpose:
        *        Execute ClearCommError of AdvCan.
        *		
        *
        *    Arguments:
        *        ErrorCode      - error code if the CAN Controller occur error
        * 
        * 
        *    Returns:
        *        true SUCCESS; or false failure 
        *
        *****************************************************************************/
        public bool acClearCommError(ref uint ErrorCode)
        {
            AdvCan.COMSTAT lpState = new AdvCan.COMSTAT();
            return AdvCan.ClearCommError(hDevice, out ErrorCode, out lpState);
        }
        /*****************************************************************************
        *
        *    acSetCommMask
        *
        *    Purpose:
        *        Execute SetCommMask of AdvCan.
        *		
        *
        *    Arguments:
        *        EvtMask    - event type
        * 
        * 
        *    Returns:
        *        true SUCCESS; or false failure 
        *
        *****************************************************************************/
        public bool acSetCommMask(uint EvtMask)
        {
            if (!AdvCan.SetCommMask(hDevice, EvtMask))
            {
                int num1 = Marshal.GetLastWin32Error();
                return false;
            }
            Marshal.WriteInt32(this.events.evPtr, 0);
            return true;

        }
        /*****************************************************************************
        *
        *    acGetCommMask
        *
        *    Purpose:
        *        Execute GetCommMask of AdvCan.
        *		
        *
        *    Arguments:
        *        EvtMask     - event type
        * 
        * 
        *    Returns:
        *        true SUCCESS; or false failure 
        *
        *****************************************************************************/
        public bool acGetCommMask(ref uint EvtMask)
        {
            return AdvCan.GetCommMask(hDevice, ref EvtMask);
        }
        /*****************************************************************************
        *
        *    acWaitEvent
        *
        *    Purpose:
        *        Wait can message or error of the CAN Controller.
        *		
        *
        *    Arguments:
        *        msgRead           - managed buffer for read
        *        nReadCount        - msg number that unmanaged buffer can preserve
        *        pulNumberofRead   - real msgs have read
        *        ErrorCode         - return error code when the CAN Controller has error
        * 
        *    Returns:
        *        =0 SUCCESS; or <0 failure 
        *
        *****************************************************************************/
        public int acWaitEvent(AdvCan.canmsg_t[] msgRead, uint nReadCount, ref uint pulNumberofRead, ref uint ErrorCode)
        {
            int nRet = OPERATION_ERROR;
            ErrorCode = 0;
            pulNumberofRead = 0;
            if (AdvCan.WaitCommEvent(hDevice, this.events.evPtr, this.events.olPtr) == true)
            {
                EventCode = (uint)Marshal.ReadInt32(this.events.evPtr, 0);
                if ((EventCode & AdvCan.EV_RXCHAR) != 0)
                {
                    nRet = acCanRead(msgRead, nReadCount, ref pulNumberofRead);
                }
                if ((EventCode & AdvCan.EV_ERR) != 0)
                {
                    nRet = OPERATION_ERROR;
                    acClearCommError(ref ErrorCode);
                }
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                if (AdvCan.ERROR_IO_PENDING == err)
                {
                    if (AdvCan.GetOverlappedResult(hDevice, this.eventOvr.memPtr, out pulNumberofRead, true))
                    {
                        EventCode = (uint)Marshal.ReadInt32(this.events.evPtr, 0);
                        if ((EventCode & AdvCan.EV_RXCHAR) != 0)
                        {
                            nRet = acCanRead(msgRead, nReadCount, ref pulNumberofRead);
                        }
                        if ((EventCode & AdvCan.EV_ERR) != 0)
                        {
                            nRet = OPERATION_ERROR;
                            acClearCommError(ref ErrorCode);
                        }
                    }
                    else
                        nRet = OPERATION_ERROR;
                }
                else
                    nRet = OPERATION_ERROR;
            }

            return nRet;
        }
    }

    internal class Win32Events
    {
        // Methods
        internal Win32Events(IntPtr ol)
        {
            this.olPtr = ol;
            this.evPtr = Marshal.AllocHGlobal(sizeof(uint));
            Marshal.WriteInt32(this.evPtr, 0);
        }

        ~Win32Events()
        {
            Marshal.FreeHGlobal(this.evPtr);
        }

        public IntPtr evPtr;
        public IntPtr olPtr;
    }

    internal class Win32Ovrlap
    {
        // Methods
        internal Win32Ovrlap(IntPtr evHandle)
        {
            this.ol = new AdvCan.OVERLAPPED();
            this.ol.offset = 0;
            this.ol.offsetHigh = 0;
            this.ol.hEvent = evHandle;
            if (evHandle != IntPtr.Zero)
            {
                this.memPtr = Marshal.AllocHGlobal(Marshal.SizeOf(this.ol));
                Marshal.StructureToPtr(this.ol, this.memPtr, false);
            }
        }

        ~Win32Ovrlap()
        {
            if (this.memPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.memPtr);
            }
        }

        public IntPtr memPtr;
        public AdvCan.OVERLAPPED ol;

    }

    #endregion
    #region class AdvCan
    // #############################################################################
    // *****************************************************************************
    //                  Copyright (c) 2011, Advantech Automation Corp.
    //      THIS IS AN UNPUBLISHED WORK CONTAINING CONFIDENTIAL AND PROPRIETARY
    //               INFORMATION WHICH IS THE PROPERTY OF ADVANTECH AUTOMATION CORP.
    //
    //    ANY DISCLOSURE, USE, OR REPRODUCTION, WITHOUT WRITTEN AUTHORIZATION FROM
    //               ADVANTECH AUTOMATION CORP., IS STRICTLY PROHIBITED.
    // *****************************************************************************

    // #############################################################################
    //
    // File:    AdvCan.cs
    // Created: 7/21/2007
    // Revision:6/5/2009
    // Version: 1.0
    //          - Initial version
    //          2.0
    //          - Compatible with 64-bit and 32-bit system
    //          2.1 (2011-5-19)
    //          - Fix bug of API declaration
    // Description: Defines data structures and function declarations
    //
    // -----------------------------------------------------------------------------

    public class AdvCan
    {
        public const int CAN_MSG_LENGTH = 22;                        //Length of canmsg_t in bytes
        public const int CAN_COMMAND_LENGTH = 24;                    //Length of Command_par_t in bytes
        public const int CAN_CONFIG_LENGTH = 24;                     //Length of Config_par_t in bytes
        public const int CAN_CANSTATUS_LENGTH = 72;                  //Length of CanStatusPar_t in bytes



        // -----------------------------------------------------------------------------
        // DESCRIPTION: Standard baud  
        // -----------------------------------------------------------------------------
        public const uint CAN_TIMING_10K = 10;
        public const uint CAN_TIMING_20K = 20;
        public const uint CAN_TIMING_50K = 50;
        public const uint CAN_TIMING_100K = 100;
        public const uint CAN_TIMING_125K = 125;
        public const uint CAN_TIMING_250K = 250;
        public const uint CAN_TIMING_500K = 500;
        public const uint CAN_TIMING_800K = 800;
        public const uint CAN_TIMING_1000K = 1000;

        // -----------------------------------------------------------------------------
        // DESCRIPTION: Acceptance filter mode  
        // -----------------------------------------------------------------------------
        public const uint PELICAN_SINGLE_FILTER = 1;
        public const uint PELICAN_DUAL_FILTER = 0;

        // -----------------------------------------------------------------------------
        // DESCRIPTION: CAN data length  
        // -----------------------------------------------------------------------------
        public const ushort DATALENGTH = 8;

        // -----------------------------------------------------------------------------
        // DESCRIPTION: For CAN frame id. if flags of frame point out 
        // some errors(MSG_OVR, MSG_PASSIVE, MSG_BUSOFF, MSG_BOUR), 
        // then id of frame is equal to ERRORID 
        // -----------------------------------------------------------------------------
        public const uint ERRORID = 0xffffffff;

        // -----------------------------------------------------------------------------
        // DESCRIPTION: CAN frame flag  
        // -----------------------------------------------------------------------------
        public const ushort MSG_RTR = (1 << 0);                    //RTR Message 
        public const ushort MSG_OVR = (1 << 1);                    //CAN controller Msg overflow error
        public const ushort MSG_EXT = (1 << 2);                    //Extended message format  
        public const ushort MSG_SELF = (1 << 3);                   //Message received from own tx 
        public const ushort MSG_PASSIVE = (1 << 4);                //CAN Controller in error passive
        public const ushort MSG_BUSOFF = (1 << 5);                 //CAN Controller Bus Off    
        public const ushort MSG_BOVR = (1 << 7);                   //Receive buffer overflow

        // -----------------------------------------------------------------------------
        // DESCRIPTION: CAN frame use by driver 
        // -----------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct canmsg_t
        {
            public int flags;                                       //Flags, indicating or controlling special message properties 
            public int cob;                                         //CAN object number, used in Full CAN
            public uint id;                                         //CAN message ID, 4 bytes  
            public short length;                                      //Number of bytes in the CAN message 
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;
        }

        // -----------------------------------------------------------------------------
        // DESCRIPTION:IOCTL Command cmd targets
        // -----------------------------------------------------------------------------
        public const ushort CMD_START = 1;                           //Start chip 
        public const ushort CMD_STOP = 2;                            //Stop chip
        public const ushort CMD_RESET = 3;                           //Reset chip 
        public const ushort CMD_CLEARBUFFERS = 4;                    //Clear the receive buffer 

        // -----------------------------------------------------------------------------
        // DESCRIPTION: IOCTL Configure cmd targets
        // -----------------------------------------------------------------------------
        public const ushort CONF_ACC = 0;                             //Accept code and mask code
        public const ushort CONF_ACCM = 1;                            //Mask code 
        public const ushort CONF_ACCC = 2;                            //Accept code 
        public const ushort CONF_TIMING = 3;                          //Bit timing 
        public const ushort CONF_LISTEN_ONLY_MODE = 8;               //For SJA1000 PeliCAN 
        public const ushort CONF_SELF_RECEPTION = 9;                 //Self reception 
        public const ushort CONF_TIMEOUT = 13;                       //Configure read and write timeout one time 
        public const ushort CONF_ACC_FILTER = 20;                    //Acceptance filter mode: 1-Single, 0-Dual 


        // -----------------------------------------------------------------------------
        // DESCRIPTION:For ulStatus of CanStatusPar_t
        // -----------------------------------------------------------------------------
        public const ushort STATUS_OK = 0;
        public const ushort STATUS_BUS_ERROR = 1;
        public const ushort STATUS_BUS_OFF = 2;

        //------------------------------------------------------------------------------
        // DESCRIPTION: For EventMask of CanStatusPar_t
        //------------------------------------------------------------------------------
        public const uint EV_ERR = 0x0080;             // Line status error occurred
        public const uint EV_RXCHAR = 0x0001;                // Any Character received

        //------------------------------------------------------------------------------
        // DESCRIPTION: For windows error code
        //------------------------------------------------------------------------------
        public const uint ERROR_SEM_TIMEOUT = 121;
        public const uint ERROR_IO_PENDING = 997;
        //------------------------------------------------------------------------------
        // DESCRIPTION: Define windows  macro used in widows API
        //------------------------------------------------------------------------------
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint GENERIC_EXECUTE = 0x20000000;
        public const uint GENERIC_ALL = 0x10000000;

        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint FILE_SHARE_DELETE = 0x4;

        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        public const uint CE_RXOVER = 0x0001;      //Receive Queue overflow
        public const uint CE_OVERRUN = 0x0002;     //Receive Overrun Error
        public const uint CE_FRAME = 0x0008;       //Receive Framing error
        public const uint CE_BREAK = 0x0010;       //Break Detected

        //------------------------------------------------------------------------------
        // DESCRIPTION: IOCTL code 
        //------------------------------------------------------------------------------
        public const uint CAN_IOCTL_COMMAND = 0x222540;
        public const uint CAN_IOCTL_CONFIG = 0x222544;
        public const uint CAN_IOCTL_STATUS = 0x222554;

        //----------------------------------------------------------------------------
        //DESCRIPTION: IOCTL Command request parameter structure 
        //----------------------------------------------------------------------------
        public struct Command_par_t
        {
            public int cmd;                          //special driver command
            public int target;                       //special configuration target 
            public uint val1;                        //parameter 1
            public uint val2;                        //parameter 2 
            public int errorv;                       //return value
            public int retval;                       //return value
        }

        //----------------------------------------------------------------------------
        //DESCRIPTION: IOCTL configuration request parameter structure 
        //----------------------------------------------------------------------------
        public struct Config_par_t
        {
            public int cmd;                          //special driver command
            public int target;                       //special configuration target 
            public uint val1;                        //parameter 1
            public uint val2;                        //parameter 2 
            public int errorv;                       //return value
            public int retval;                       //return value
        }

        // -----------------------------------------------------------------------------
        //DESCRIPTION:IOCTL Generic CAN controller status request parameter structure 
        // -----------------------------------------------------------------------------
        public struct CanStatusPar_t
        {
            public uint baud;                      //Actual bit rate 
            public uint status;                    //CAN controller status register 
            public uint error_warning_limit;       //The error warning limit 
            public uint rx_errors;                 //Content of RX error counter
            public uint tx_errors;                 //Content of TX error counter 
            public uint error_code;                //Content of error code register 
            public uint rx_buffer_size;            //Size of rx buffer
            public uint rx_buffer_used;            //number of messages
            public uint tx_buffer_size;            //Size of tx buffer for wince, windows not use tx buffer
            public uint tx_buffer_used;            //Number of message for wince, windows not use tx buffer s
            public uint retval;                    //Return value
            public uint type;                      //CAN controller/driver type
            public uint acceptancecode;            //Acceptance code 
            public uint acceptancemask;            //Acceptance mask
            public uint acceptancemode;             //Acceptance Filter Mode: 1:Single 0:Dual
            public uint selfreception;             //Self reception 
            public uint readtimeout;               //Read timeout 
            public uint writetimeout;              //Write timeout 
        }

        //----------------------------------------------------------------------------
        //DESCRIPTION: Asynchronous OVERLAPPED structure
        //----------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        internal protected struct OVERLAPPED
        {
            internal UIntPtr internalLow;
            internal UIntPtr internalHigh;
            internal uint offset;
            internal uint offsetHigh;
            internal IntPtr hEvent;
        }
        //----------------------------------------------------------------------------
        //DESCRIPTION: COMSTAT  structure
        //----------------------------------------------------------------------------
        public struct COMSTAT
        {
            public int fCtsHold;
            public int fDsrHold;
            public int fRlsdHold;
            public int fXoffHold;
            public int fXoffSent;
            public int fEof;
            public int fTxim;
            public int fReserved;
            public int cbInQue;
            public int cbOutQue;
        }


        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out uint nNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WaitCommEvent(IntPtr hFile, IntPtr lpEvtMask, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hDevice, IntPtr pbData, uint nNumberOfFramesToRead, out uint lpNumberOfFramesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hDevice, AdvCan.canmsg_t[] msgWrite, uint nNumberOfFramesToWrite, out uint lpNumberOfFramesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, ref int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        public static extern bool ClearCommError(IntPtr hFile, out uint lpErrors, out COMSTAT cs);

        [DllImport("kernel32.dll")]
        public static extern bool GetCommMask(IntPtr hFile, ref uint EvtMask);

        [DllImport("kernel32.dll")]
        public static extern bool SetCommMask(IntPtr hFile, uint dwEvtMask);

    }

    #endregion

    
    #endregion
    #region ECANConverter
    public class ECANConverter : IUCANConverter
    {
        public event MyDelegate ErrEvent;
        public event MyDelegate Progress;

        #region Всякая шняга
        public const int CAN200_OK = 0;	//< Операция завершена успешно
        public const int CAN200_ERR_PARM = -1;	//< Ошибка входных параметров
        public const int CAN200_ERR_SYS = -2;	//< Системная ошибка
        public const int CAN200_ERR_MODE = -3;	//< Режим работы не соответствует требуемому
        public const int CAN200_ERR_BUSY = -4;	//< Буфер выдачи занят

        public const int NUM_CHANNEL = 2;	//< Количество каналов на плате

        public const int CAN_CHANNEL_1 = 1;	//<Первый канал платы
        public const int CAN_CHANNEL_2 = 2;	//<Второй канал платы

        // Максимальное количество плат.
        public const int MAX_CAN_NUMBER = 10;

        public const int BasicCAN = 0;	///< Основной режим
        public const int PeliCAN = 1;	///< Расширенный режим

        //Speed
        int CAN_SPEED_USER_DEFINED(int btr0, int btr1)		//< Скорость определенная пользователем
        {
            return 0xffff | (((btr0) & 0xff) << 24) | (((btr1) & 0xff) << 16);
        }
        int IS_CAN_SPEED_USER_DEFINED(int speed)	//< 1 - скорость определяемая пользователем, 0 - одна из стандартных скоростей
        {
            return (0xffff == ((speed) & 0xffff)) ? 1 : 0;
        }
        int CAN_SPEED_GET_BTR0(int speed)
        {
            return ((speed) >> 24) & 0xff;	//< Возвращает значение регсистра BTR0 для скоростей задаваемых пользователем
        }
        int CAN_SPEED_GET_BTR1(int speed) //< Возвращает значение регсистра BTR1 для скоростей задаваемых пользователем 
        {
            return ((speed) >> 16) & 0xff;
        }
        public const int CAN_SPEED_1000 = 1000;	//< Скорость 1 Mbit/sec 
        public const int CAN_SPEED_800 = 800;		//< Скорость 800 kbit/sec 
        public const int CAN_SPEED_500 = 500;		//< Скорость 500 kbit/sec 
        public const int CAN_SPEED_250 = 250;		//< Скорость 250 kbit/sec 
        public const int CAN_SPEED_125 = 125;		//< Скорость 125 kbit/sec 
        public const int CAN_SPEED_50 = 50;		//< Скорость 50 kbit/sec 
        public const int CAN_SPEED_20 = 20;		//< Скорость 20 kbit/sec 
        public const int CAN_SPEED_10 = 10;		//< Скорость 10 kbit/sec 

        public const int FILE_DEVICE_CAN200 = 0x8000;

        public const int V_HardReset = 0;
        public const int V_SetWorkMode = 1;
        public const int V_GetWorkMode = 2;
        public const int V_SetDriverMode = 3;
        public const int V_SetCANSpeed = 4;
        public const int V_GetStatus = 5;
        public const int V_SetInterruptSource = 6;
        public const int V_SetTxBuffer = 7;
        public const int V_GetRxBuffer = 8;
        public const int V_SetCommand = 9;
        public const int V_B_SetInputFilter = 10;
        public const int V_P_SetRxErrorCounter = 11;
        public const int V_P_GetRxErrorCounter = 12;
        public const int V_P_SetTxErrorCounter = 13;
        public const int V_P_GetTxErrorCounter = 14;
        public const int V_P_SetErrorWarningLimit = 15;
        public const int V_P_GetErrorWarningLimit = 16;
        public const int V_P_GetArbitrationLostCapture = 17;
        public const int V_P_GetRxMessageCounter = 18;
        public const int V_P_GetErrorCode = 19;
        public const int V_P_SetInputFilter = 20;
        public const int V_DefEvent = 21;
        public const int V_GetEventData = 22;
        public const int V_GetConfig = 23;
        public const int V_GetCANSpeed = 24;
        public const int V_B_GetInputFilter = 25;
        public const int V_P_GetInputFilter = 26;
        public const int V_SetCANReg = 27;
        public const int V_GetCANReg = 28;
        public const int V_GetInterruptSource = 29;
        public const int V_GetOverCounter = 30;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TUniCanMessage
        {
            public UInt32 messageID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public Byte[] data;
            public Byte length;
            //служебные идентификаторы
            public Byte RTR;		//признак RTR
            public Byte EFF;		//признак расширенного идентификатора (29 бит)
            public UInt32 time;
        };// END STRUCTURE DEFINITION TCanOlsMessage

        public enum CAN_ChannelBaudRate
        {
            CAN_BR_1000k,
            CAN_BR_800k,
            CAN_BR_500k,
            CAN_BR_250k,
            CAN_BR_125k,
            CAN_BR_50k,
            CAN_BR_20k,
            CAN_BR_10k,
            CAN_BR_ALL
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TCAN_VPDData
        {
            //Имя платы Строка вида CAN-200PCI(e) vX.Y, где vX.Y – номер версии платы
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public Byte[] szName;
            // Серийный номер платы
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public char[] szSN;
            //Базовый адрес первого канала
            public ushort wPorts1;
            //Базовый адрес второго канала
            public ushort wPorts2;
            //Номер вектора прерывания
            public ushort wIRQ;
        };
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct RX_TX_Buffer
        {
            // #BasicCAN - стандартный кадр #PeliCAN - расширенный кадр
            public ushort FF; //Стандартный идентификатор 11-ти битный идентификатор принятого кадра (действителен только при #FF = #BasicCAN)
            public ushort sID;         //стандартный идентификатор (11 бит) 
            //Расширенный идентификатор 29-ти битный идентификатор принятого кадра (действителен только при #FF = #PeliCAN)
            public uint extID;
            //Значение бита RTR (0 или 1)
            public ushort RTR;         // RTR - бит                         //
            //Количество принятых/выдаваемых байт данных (0-8)
            public ushort DLC;
            //Выдаваемые/принимаемые данные (от 0 до 8 байт) Реальное количество принимаемых/выдаваемых данных определяется полем #DLC
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public ushort[] DataByte;
            public RX_TX_Buffer(int i)
                : this()
            {
                DataByte = new ushort[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
                FF = 0;
                sID = 0;
                extID = 0;
                RTR = 0;
                DLC = 0;
            }
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TEventData
        {
            //Принятый кадр Поля структуры содержат действительные значения только при #IntrID = 1
            public RX_TX_Buffer rxtxbuf;
            //Причина установки события Содержимое регистра идентификации прерывания CAN-контроллера канала
            public char IntrID;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct bFilter_t
        {
            //Идентификатор входного фильтра
            public ushort Code;
            //Маска входного фильтра
            public ushort Mask;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct pFilter_t
        {
            //Идентификатор входного фильтра
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] Filter;
            //Маска входного фильтра
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] Mask;
            //Режим функционирования входного фильтра 0 - одиночный фильтр 1 - двойной фильтр
            public ushort Mode;
        };
        [Flags]
        public enum bit : int
        {
            rbs = 1,
            dos = 2,
            tbs = 4,
            tcs = 8,
            rs = 16,
            ts = 32,
            es = 64,
            bs = 128
        }

        //        [Flags]
        public enum bit2 : int
        {
            rbs = 1,
            dos = 2,
            tbs = 4,
            tcs = 8,
            rs = 16,
            ts = 32,
            es = 64,
            bs = 128
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        public struct CAN_status_t
        {
            [FieldOffset(0)]
            public bit _bit;
            [FieldOffset(0)]
            public int _byte;
        };
        #endregion
        #region Импорт функций из ДЛЛ

        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetNumberDevice(ref int count);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern HANDLE CAN200_Open(int number);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_Close(HANDLE Handle);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetWorkMode(HANDLE Handle, int Channel, int Mode);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetWorkMode(HANDLE Handle, int Channel, ref int Mode);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetDriverMode(HANDLE Handle, int Channel, int Mode);
        //
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetConfig(HANDLE Handle, ref TCAN_VPDData Buffer);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetCANSpeed(HANDLE Handle, int Channel, uint Speed);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetCANSpeed(HANDLE Handle, int Channel, ref uint Speed);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetStatus(HANDLE Handle, int Channel, ref int Status);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetInterruptSource(HANDLE Handle, int Channel, int Source);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetInterruptSource(HANDLE Handle, int Channel, ref int Source);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetCommand(HANDLE Handle, int Channel, int Command);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetTxBuffer(HANDLE Handle, int Channel, ref RX_TX_Buffer Buffer);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_DefEvent(HANDLE Handle, int Channel, HANDLE hEvent);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetEventData(HANDLE Handle, int Channel, ref TEventData Buffer);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetRxBuffer(HANDLE Handle, int Channel, ref RX_TX_Buffer Buffer);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_B_SetInputFilter(HANDLE Handle, int Channel, ref bFilter_t filter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_B_GetInputFilter(HANDLE Handle, int Channel, ref bFilter_t filter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_SetInputFilter(HANDLE Handle, int Channel, ref pFilter_t filter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetInputFilter(HANDLE Handle, int Channel, ref pFilter_t filter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_SetRxErrorCounter(HANDLE Handle, int Channel, int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetRxErrorCounter(HANDLE Handle, int Channel, ref int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_SetTxErrorCounter(HANDLE Handle, int Channel, int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetTxErrorCounter(HANDLE Handle, int Channel, ref int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_SetErrorWarningLimit(HANDLE Handle, int Channel, int Limit);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetErrorWarningLimit(HANDLE Handle, int Channel, ref int Limit);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetArbitrationLostCapture(HANDLE Handle, int Channel, ref int Data);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetRxMessageCounter(HANDLE Handle, int Channel, ref int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_P_GetErrorCode(HANDLE Handle, int Channel, ref int Code);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetOverCounter(HANDLE Handle, int Channel, ref int Counter);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_SetCANReg(HANDLE Handle, int Channel, int Port, int Data);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetCANReg(HANDLE Handle, int Channel, int Port, ref int Data);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_Recv(HANDLE Handle, int Channel, ref RX_TX_Buffer Buffer, int timeout);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_HardReset(HANDLE Handle, int Channel);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_GetLastError();
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt64 CAN200_GetAPIVer();
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_RecvPack(HANDLE Handle, int Channel, ref int count, int timeout);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Byte CAN200_GetByte(int num);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void CAN200_Recv_Enable(HANDLE Handle, int Channel, int timeout);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void CAN200_Recv_Disable();
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_Pop(ref RX_TX_Buffer buf);
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_VecSize();
        [DllImport(@"elcusCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CAN200_ClearBuf(HANDLE Handle, int Channel);
        #endregion

        HANDLE hCan = IntPtr.Zero;

        int port = CAN_CHANNEL_1;
        uint speed = CAN_SPEED_1000;
        int mode = PeliCAN;
        //        int mode = BasicCAN;
        public ECANConverter()
        {
            Port = 0;
            Speed = 0;
            Is_Open = false;
        }
        public String Info
        {
            get;
            set;
        }
        public Byte Port
        {
            get;
            set;
        }
        public Byte Speed
        {
            get;
            set;
        }
        public Boolean Is_Open
        {
            get;
            set;
        }
        public Boolean Is_Present
        {
            get
            {
                int result;
                int count = 0;
                result = CAN200_GetNumberDevice(ref count);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Плата не установлена!"));
                    return false;
                }
                if (0 < count)
                {
                    return true;
                }
                return false;
            }
            set
            {
            }
        }
        public String GetAPIVer
        {
            get
            {
                UInt64 ver = CAN200_GetAPIVer(); //#define VER 0x0000290120161346
                int a1 = (int)(ver >> 40);
                int a2 = (int)(ver >> 32) & 0xFF;
                int a3 = (int)(ver >> 16) & 0xFFFF;
                int a4 = (int)(ver >> 8) & 0xFF;
                int a5 = (int)(ver & 0xFF);

                return a1.ToString("X02") + "." + a2.ToString("X02") + "." + a3.ToString("X04") + " " + a4.ToString("X02") + ":" + a5.ToString("X02");
            }
            set { }
        }
        public Boolean Open()
        {

            hCan = CAN200_Open(0);
            if (null == hCan)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Ошибка открытия контроллера"));
                return false;
            }
            int result = 0;

            TCAN_VPDData buf = new TCAN_VPDData();
            result = CAN200_GetConfig(hCan, ref buf);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу прочитать конфиг"));
                return false;
            }
            Info = Encoding.Default.GetString(buf.szName, 0, 128);
            Info = "Elcus " + Info.Substring(0, Info.IndexOf('\0')).Trim();

            if (Port == 0)
                port = CAN_CHANNEL_1;
            else
                port = CAN_CHANNEL_2;

            result = CAN200_SetWorkMode(hCan, 1, mode);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу установить режим 1"));
                return false;
            }
            result = CAN200_GetWorkMode(hCan, 1, ref mode);
            Trace.WriteLine("-- WorkMode 1 = " + mode.ToString() + " --");

            // Разрешаем работы выходных формирователей
            result = CAN200_SetDriverMode(hCan, 1, 0x1B);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу установить фильтр 1"));
                return false;
            }
            // Разрешаем прием всех кадров
            pFilter_t filterP = new pFilter_t();
            filterP.Filter = new ushort[4];
            filterP.Mask = new ushort[4];
            filterP.Filter[0] = filterP.Filter[1] = filterP.Filter[2] = filterP.Filter[3] = 0xff;
            filterP.Mask[0] = filterP.Mask[1] = filterP.Mask[2] = filterP.Mask[3] = 0xff;
            filterP.Mode = 0;

            bFilter_t filterB = new bFilter_t();
            filterB.Code = 0x00;
            filterB.Mask = 0xff;

            if (mode == BasicCAN)
            {
                result = CAN200_B_SetInputFilter(hCan, 1, ref filterB);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не могу установить фильтр 1"));
                    return false;
                }
            }
            else
            {
                result = CAN200_P_SetInputFilter(hCan, 1, ref filterP);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не могу установить фильтр 1"));
                    return false;
                }
            }

            switch (Speed)
            {
                case 0:
                    speed = CAN_SPEED_1000;
                    break;
                case 1:
                    speed = CAN_SPEED_800;
                    break;
                case 2:
                    speed = CAN_SPEED_500;
                    break;
                case 3:
                    speed = CAN_SPEED_250;
                    break;
                case 4:
                    speed = CAN_SPEED_125;
                    break;
                case 5:
                    speed = CAN_SPEED_50;
                    break;
                case 6:
                    speed = CAN_SPEED_20;
                    break;
                case 7:
                    speed = CAN_SPEED_10;
                    break;
                default:
                    speed = CAN_SPEED_1000;
                    break;
            }

            result = CAN200_SetCANSpeed(hCan, 1, speed);
            speed = 0;
            result = CAN200_GetCANSpeed(hCan, 1, ref speed);
            Trace.WriteLine("-- speed 1 = " + speed.ToString() + " --");
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу установить скорость 1"));
                return false;
            }

            if (mode == PeliCAN)
            {
                result = CAN200_P_SetErrorWarningLimit(hCan, 1, 255);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не могу установить лимит ошибок 1"));
                    return false;
                }
            }
            // Разрешаем прерывания по приему кадра
            result = CAN200_SetInterruptSource(hCan, port, 0x01);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу включить прерывание"));
                return false;
            }

            //CAN200_GetOverCounter(hCan, port, ref result);
            //Trace.WriteLine("!!GetOverCounter " + result.ToString());

            Is_Open = true;
            //Recv_Enable();
            return true;
        }
        public void Close()
        {
            try
            {
                CAN200_Close(hCan);
            }
            catch (Exception)
            {

            }
            Is_Open = false;
        }
        public Boolean Send(ref canmsg_t msg)
        {
            int result = 0;
            CAN200_HardReset(hCan, port);
            if (mode == PeliCAN)
            {
                result = CAN200_P_SetTxErrorCounter(hCan, port, 0);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не удалось сбросить счетчик ошибок " + result.ToString()));
                    Trace.WriteLine("Не удалось сбросить счетчик ошибок " + result.ToString());
                    return false;
                }
            }
            result = CAN200_SetCommand(hCan, port, 6);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось очистить буфер " + result.ToString()));
                Trace.WriteLine("Не удалось очистить буфер " + result.ToString());
                return false;
            }
            RX_TX_Buffer buf = new RX_TX_Buffer(1);
            buf.sID = (ushort)msg.id;
            buf.DLC = msg.len;
            buf.FF = 0;

            for (int i = 0; i < msg.len; i++)
                buf.DataByte[i] = msg.data[i];

#if DDDE
            Trace.Write("Send data: ");
            print_RX_TX(buf);
#endif
            result = CAN200_SetTxBuffer(hCan, port, ref buf);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось поместить пакет в буфер " + result.ToString()));
                Trace.WriteLine("Не удалось поместить пакет в буфер " + result.ToString());
                return false;
            }
            result = CAN200_SetCommand(hCan, port, 1);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение 3 " + result.ToString()));
                Trace.WriteLine("Не удалось отправить сообщение 3 " + result.ToString());
                return false;
            }
            int to = 2000;
            do
            {
                CAN200_GetStatus(hCan, port, ref result);
                //                Trace.WriteLine("No send finished " + to.ToString() + " " + ((bit)result).ToString() + " tbs=" + (result & 4).ToString() + " tcs=" + (result & 8).ToString());
                Thread.Sleep(1);
            } while (((result & 4) == 0) & ((result & 8) == 0) & (to-- > 0));
            if (to <= 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение 4 " + ((bit)result).ToString()));
                Trace.WriteLine("Не удалось отправить сообщение 4 " + ((bit)result).ToString());
                return false;
            }
            return true;
        }
        public Boolean Send(ref canmsg_t msg, int timeout)
        {
            int result = 0;
            CAN200_HardReset(hCan, port);
            if (mode == PeliCAN)
            {
                result = CAN200_P_SetTxErrorCounter(hCan, port, 0);
                if (CAN200_OK != result)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не удалось сбросить счетчик ошибок " + result.ToString()));
                    Trace.WriteLine("Не удалось сбросить счетчик ошибок " + result.ToString());
                    return false;
                }
            }
            result = CAN200_SetCommand(hCan, port, 6);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось очистить буфер " + result.ToString()));
                Trace.WriteLine("Не удалось очистить буфер " + result.ToString());
                return false;
            }
            RX_TX_Buffer buf = new RX_TX_Buffer(1);
            buf.sID = (ushort)msg.id;
            buf.DLC = msg.len;
            buf.FF = 0;

            for (int i = 0; i < msg.len; i++)
                buf.DataByte[i] = msg.data[i];
//            Clear_RX();
#if DDDE
            Trace.Write("Send data: ");
            print_RX_TX(buf);
#endif
            result = CAN200_SetTxBuffer(hCan, port, ref buf);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось поместить пакет в буфер " + result.ToString()));
                Trace.WriteLine("Не удалось поместить пакет в буфер " + result.ToString());
                return false;
            }
            result = CAN200_SetCommand(hCan, port, 1);
            if (CAN200_OK != result)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение 3 " + result.ToString()));
                Trace.WriteLine("Не удалось отправить сообщение 3 " + result.ToString());
                return false;
            }
            int to = (int)timeout;
            do
            {
                CAN200_GetStatus(hCan, port, ref result);
                //                Trace.WriteLine("No send finished " + to.ToString() + " " + ((bit)result).ToString() + " tbs=" + (result & 4).ToString() + " tcs=" + (result & 8).ToString());
                Thread.Sleep(1);
            } while (((result & 4) == 0) & ((result & 8) == 0) & (to-- > 0));
            if (to <= 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение 4 " + ((bit)result).ToString()));
                Trace.WriteLine("Не удалось отправить сообщение 4 " + ((bit)result).ToString());
                return false;
            }
            return true;
        }
        public Boolean Recv(ref canmsg_t msg, int timeout)
        {
            int result = 0;
            int i = 0, j = 0;
            RX_TX_Buffer ret = new RX_TX_Buffer(1);
            do
            {
                result = CAN200_VecSize();
                Thread.Sleep(1);
            } while (result < 1 && (Int32)timeout-- > 0);
            if ((Int32)timeout <= 0)
            {
                Trace.WriteLine("Recv Error timeout");
                return false;
            }
#if DDDE
            Trace.WriteLine("VecSize1 = " + CAN200_VecSize().ToString() + " Res = " + result.ToString() + " to = " + ((Int32)timeout).ToString());
#endif
            CAN200_Pop(ref ret);
            for (j = 0; j < ret.DLC; j++)
            {
                msg.data[j] = (Byte)ret.DataByte[j];
            }
            msg.id = ret.sID;
            msg.len = (Byte)ret.DLC;
#if DDDE
            Trace.WriteLine("VecSize2 = " + CAN200_VecSize().ToString());
#endif
#if DDDE
            Trace.WriteLine("Recv byte count = " + j.ToString());
            Trace.Write("Recv: ");
            print_RX_TX(ret);
#endif
#if DDDE
            Trace.WriteLine("VecSize3 = " + CAN200_VecSize().ToString());
#endif
            return true;
        }
        public Boolean RecvPack(ref Byte[] arr, ref int count, int timeout)
        {
            int result = 0;
            int i = 0, j = 0;
            RX_TX_Buffer msg = new RX_TX_Buffer(1);
            do
            {
                result = CAN200_VecSize();
                Thread.Sleep(1);
            } while (result < count && timeout-- > 0);
#if DDDE
            Trace.WriteLine("res = " + result.ToString() + " to = " + timeout.ToString());
#endif
            if (timeout <= 0)
            {
                Trace.WriteLine("ELCAN RecvPack Error timeout");
                return false;
            }
            for (i = 0; i < count; i++)
            {
                if(CAN200_VecSize() > 0)
                    CAN200_Pop(ref msg);
#if DDDE
//                print_RX_TX(msg);
#endif
                for (j = 0; j < msg.DLC; j++)
                {
                    if ((i * 8 + j) < arr.Length)
                        arr[i * 8 + j] = (Byte)msg.DataByte[j];
                }
                Application.DoEvents();
                if (Progress != null)
                    Progress(this, new MyEventArgs(i));
//                Application.DoEvents();
            }
#if DDDE
            Trace.WriteLine("Recv pack count = " + i.ToString());
            Trace.WriteLine("Recv byte count = " + ((i - 1) * 8 + msg.DLC).ToString());
#endif
            return true;
        }
        public Boolean SendCmd(ref canmsg_t msg, int timeout)
        {
            if (!Send(ref msg))
                return false;
            if (!Recv(ref msg, timeout))
                return false;
            return true;
        }
        public int GetStatus()
        {
            int result = 0;
            CAN200_GetStatus(hCan, port, ref result);
            return result;
        }
        ~ECANConverter()
        {
            if (Is_Open)
                CAN200_Close(hCan);
        }
        public String ErrDecode(int err)
        {
            int e1, e2, e3;
            e1 = err & 0x001F;
            e2 = (err >> 5) & 0x0001;
            e3 = (err >> 6) & 0x0003;
            String sss = "";
            switch (e1)
            {
                case 2:
                    sss += "идентификатор: биты 28-21, ";
                    break;
                case 3:
                    sss += "начало кадра, ";
                    break;
                case 4:
                    sss += "бит SRTR, ";
                    break;
                case 5:
                    sss += "бит IDE, ";
                    break;
                case 6:
                    sss += "идентификатор: биты 20-18, ";
                    break;
                case 7:
                    sss += "идентификатор: биты 17-13, ";
                    break;
                case 8:
                    sss += "CRC последовательность, ";
                    break;
                case 9:
                    sss += "зарезервированный бит 0, ";
                    break;
                case 10:
                    sss += "поле данных, ";
                    break;
                case 11:
                    sss += "код длины данных, ";
                    break;
                case 12:
                    sss += "бит RTR, ";
                    break;
                case 13:
                    sss += "зарезервированный бит 1, ";
                    break;
                case 14:
                    sss += "идентификатор: биты 4-0, ";
                    break;
                case 15:
                    sss += "идентификатор: биты 12-5, ";
                    break;
                case 17:
                    sss += "флаг активной ошибки, ";
                    break;
                case 18:
                    sss += "перерыв на шине, ";
                    break;
                case 19:
                    sss += "tolerate dominant bits, ";
                    break;
                case 22:
                    sss += "флаг пассивной ошибки, ";
                    break;
                case 23:
                    sss += "разделитель ошибки, ";
                    break;
                case 24:
                    sss += "разделитель CRC, ";
                    break;
                case 25:
                    sss += "интервал подтверждения, ";
                    break;
                case 26:
                    sss += "конец кадра, ";
                    break;
                case 27:
                    sss += "разделитель подтверждения, ";
                    break;
                case 28:
                    sss += "флаг переполнения, ";
                    break;
                default:
                    sss += "ХЗ, что за ошибка, ";
                    break;
            }

            if (e2 == 0)
                sss += "TX (ошибка в течение выдачи), ";
            else
                sss += "RX (ошибка в течение приема), ";

            switch (e3)
            {
                case 0:
                    sss += "0 - битовая ошибка";
                    break;
                case 1:
                    sss += "1 - ошибка формы";
                    break;
                case 2:
                    sss += "2 - ошибка заполнения";
                    break;
                default:
                    sss += "3 - другая ошибка";
                    break;
            }
            return "Error " + err.ToString() + " " + sss;
        }
        void print_RX_TX(RX_TX_Buffer bb)
        {
            Trace.Write(" ID=" + bb.sID.ToString("X2") + " len=" + bb.DLC.ToString());
            Trace.Write(" Data:");
            for (int i = 0; i < bb.DLC; i++)
                Trace.Write(" 0x" + bb.DataByte[i].ToString("X2"));
            Trace.WriteLine("");
        }
        void print_RX_TX(canmsg_t mm)
        {
            Trace.Write(" ID=" + mm.id.ToString("X2") + " len=" + mm.len.ToString());
            Trace.Write(" Data:");
            for (int i = 0; i < mm.len; i++)
                Trace.Write(" 0x" + mm.data[i].ToString("X2"));
            Trace.WriteLine("");
        }

        /// <summary>
        /// для отладки 
        /// </summary>
        /// <returns></returns>
        public void GetRxErrorCounter()
        {
            int counter = 0;
            CAN200_P_GetRxErrorCounter(hCan, port, ref counter);
            Trace.WriteLine("GetRxErrorCounter = " + counter.ToString());
        }
        public void GetRxMessageCounter()
        {
            int counter = 0;
            CAN200_P_GetRxMessageCounter(hCan, port, ref counter);
            Trace.WriteLine("GetRxMessageCounter = " + counter.ToString());
        }
        public void GetEventData()
        {
            TEventData evd = new TEventData();
            CAN200_GetEventData(hCan, port, ref evd);
            Trace.Write("IntrID = " + evd.IntrID.ToString() + " ");
            print_RX_TX(evd.rxtxbuf);
        }
        int VecSize()
        {
            return CAN200_VecSize();
        }
        public int VectorSize()
        {
            return CAN200_VecSize();
        }
        int Pop()
        {
            RX_TX_Buffer buf = new RX_TX_Buffer(1);
            CAN200_Pop(ref buf);
            Trace.WriteLine("Pop ");
            print_RX_TX(buf);
            return 0;
        }
        public void Recv_Enable()
        {
            CAN200_Recv_Enable(hCan, port, 2000);
            Trace.WriteLine("Elcus Recv Enable");
        }
        public void Recv_Disable()
        {
            CAN200_Recv_Disable();
            Trace.WriteLine("Elcus Recv disable");
        }
        public void Clear_RX()
        {
            Trace.WriteLine("Cleared: " + CAN200_ClearBuf(hCan, port).ToString());
        }
        public void HWReset()
        {
            CAN200_HardReset(hCan, 0);
//            Trace.WriteLine("Hardware reset");
        }

    }
    #endregion
    #region MCANConverter
    public class MCANConverter : IUCANConverter
    {
        public event MyDelegate ErrEvent;
        public event MyDelegate Progress;

        public canerrs_t errs = new canerrs_t();
        public canwait_t cw = new canwait_t();
        public canmsg_t frame = new canmsg_t();
        Boolean flag_thr = true;
        List<canmsg_t> mbuf = new List<canmsg_t>();
        Thread thr;
        Mutex mtx = new Mutex();

        #region Импорт функций из ДЛЛ
        [DllImport("chai.dll", CallingConvention = CallingConvention.StdCall)]
        static extern Int16 CiInit();
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiOpen(Byte chan, Byte flags);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiClose(Byte chan);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiStart(Byte chan);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiStop(Byte chan);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiSetFilter(Byte chan, UInt32 acode, UInt32 amask);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiSetBaud(Byte chan, Byte bt0, Byte bt1);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiWrite(Byte chan, ref canmsg_t mbuf, Int16 cnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiTransmit(Byte chan, ref canmsg_t mbuf);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiTrCancel(Byte chan, ref UInt16 trqcnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiTrStat(Byte chan, ref UInt16 trqcnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiRead(Byte chan, ref canmsg_t mbuf, Int16 cnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiErrsGetClear(Byte chan, ref canerrs_t errs);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiWaitEvent(ref canwait_t cw, int cwcount, int tout);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiTrQueThreshold(Byte chan, Int16 getset, ref UInt16 thres);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiRcQueResize(Byte chan, UInt16 size);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiRcQueCancel(Byte chan, ref UInt16 rcqcnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiRcQueGetCnt(Byte chan, UInt16 rcqcnt);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiBoardGetSerial(Byte brdnum, ref char sbuf, UInt16 bufsize);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiHwReset(Byte chan);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiSetLom(Byte chan, Byte mode);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiWriteTout(Byte chan, Int16 getset, ref UInt16 msec);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern UInt32 CiGetLibVer();
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern UInt32 CiGetDrvVer();
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiChipStat(Byte chan, ref chipstat_t stat);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiChipStatToStr(ref chipstat_t status, ref chstat_desc_t desc);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiBoardInfo(ref canboard_t binfo);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern void CiStrError(Int16 cierrno, ref char buf, Int16 n);
        //		static extern void CiStrError(Int16 cierrno, char* buf, Int16 n);
        //		static extern void CiPerror(Int16 cierrno, const char *s);
        //		static extern Int16 CiSetCB(Byte chan, Byte ev, void (*ci_handler) (Int16));
        //		static extern Int16 CiSetCBex(Byte chan, Byte ev, void (*ci_cb_ex) (Byte, Int16, void *), void*udata);
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiCB_lock();
        [DllImport("chai.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern Int16 CiCB_unlock();
        #endregion
        public MCANConverter()
        {
            Port = 0;
            Speed = 0;
            Is_Open = false;
        }
        public String Info
        {
            set;
            get;
        }
        public Byte Port
        {
            set;
            get;
        }
        public Byte Speed
        {
            set;
            get;
        }
        public Boolean Is_Open
        {
            get;
            set;
        }
        public Boolean Is_Present
        {
            get
            {
                if (Open())
                {
                    Close();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set
            {
            }
        }
        public String GetAPIVer
        {
            get
            {
                return "";
            }
            set { }
        }
        public Boolean Open()
        {
            try
            {
                if (CiGetDrvVer() == 0)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не установлен драйвер"));
                    Is_Open = false;
                    return false;
                }
            }
            catch (Exception)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не установлен драйвер"));
                Is_Open = false;
                return false;
            }

            try
            {
                if (CiInit() < 0)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Ошибка библиотеки"));
                    return false;
                }
            }
            catch (Exception)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Ошибка библиотеки"));
                return false;
            }

            canboard_t binfo = new canboard_t();
            binfo.brdnum = 0;
            if (CiBoardInfo(ref binfo) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не подключен адаптер"));
                return false;
            }

            Info = new String(binfo.name, 3, 13) + " (" + new String(binfo.manufact, 3, 20) + ")";

            /*  open channel */
            if (CiOpen(Port, Const.CIO_CAN11 | Const.CIO_CAN29) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу открыть CAN"));
                return false;
            }

            /* set baud rate to 500Kbit */
            Byte bt0, bt1;
            switch (Speed)
            {
                case 0:
                    bt0 = 0x00;
                    bt1 = 0x14;
                    break;
                case 1:
                    bt0 = 0x00;
                    bt1 = 0x16;
                    break;
                case 2:
                    bt0 = 0x00;
                    bt1 = 0x1c;
                    break;
                case 3:
                    bt0 = 0x01;
                    bt1 = 0x1c;
                    break;
                case 4:
                    bt0 = 0x03;
                    bt1 = 0x1c;
                    break;
                case 5:
                    bt0 = 0x04;
                    bt1 = 0x1c;
                    break;
                case 6:
                    bt0 = 0x09;
                    bt1 = 0x1c;
                    break;
                case 7:
                    bt0 = 0x18;
                    bt1 = 0x1c;
                    break;
                case 8:
                    bt0 = 0x31;
                    bt1 = 0x1c;
                    break;
                default:
                    bt0 = 0x00;
                    bt1 = 0x1c;
                    break;
            }
            if (CiSetBaud(Port, bt0, bt1) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу установить скорость"));
                return false;
            }

            if (CiRcQueResize(Port, UInt16.MaxValue) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу изменить буфер"));
                return false;
            }

            CiStart(Port);
            CiHwReset(Port);
            cw.chan = Port;
            cw.wflags = Const.CI_WAIT_RC | Const.CI_WAIT_ER;
            frame.data = new Byte[8];
            canerrs_t errs = new canerrs_t();
            CiErrsGetClear(Port, ref errs);
            Is_Open = true;
//            Recv_Enable();
            return true;
        }
        public void Close()
        {
            if(thr != null)
                if(thr.IsAlive)
                    thr.Abort();
            thr = null;
            CiStop(Port);
            CiClose(Port);
            Is_Open = false;
        }
        public Boolean Send(ref canmsg_t msg)
        {
            CiStart(0);
            canerrs_t ce = new canerrs_t();
            CiErrsGetClear(Port, ref ce);

            if (CiWrite(Port, ref msg, 1) < 1)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение"));
                return false;
            }
            else
            {
#if DDDM
                Trace.Write("Send data: ");
                print_RX_TX(msg);
#endif

                return true;
            }
        }
        public Boolean Send(ref canmsg_t msg, int timeout)
        {
            CiStart(0);
            canerrs_t ce = new canerrs_t();
            CiErrsGetClear(Port, ref ce);
            if (CiWrite(Port, ref msg, 1) < 1)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение"));
                return false;
            }
            else
            {
#if DDDM
                Trace.Write("Send data: ");
                print_RX_TX(msg);
#endif
                return true;
            }
        }
        public Boolean Recv(ref canmsg_t msg, int timeout)
        {
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while(cnt < 1 && (Int32)timeout-- > 0);
            if ((Int32)timeout <= 0)
                return false;
            pop(ref msg);
#if DDDM
            Trace.Write("Recv data: ");
            print_RX_TX(msg);
            Trace.WriteLine("");
#endif
            return true;
        }
        public Boolean RecvPack(ref Byte[] arr, ref int count, int timeout)
        {
            canmsg_t mm = new canmsg_t();
            mm.data = new Byte[8];
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while (cnt < count && timeout-- > 0);
#if DDDM
            Trace.WriteLine("MCAN packets: " + cnt.ToString());
#endif
            if (timeout <= 0)
            {
                Trace.WriteLine("MCAN RecvPack Error timeout");
//                return false;
            }
            for (int i = 0; i < count; i++)
            {
//                Application.DoEvents();
                pop(ref mm);
//                print_RX_TX(mm);
                for (int j = 0; j < mm.len; j++)
                {
                    if ((i * 8 + j) < arr.Length)
                        arr[i * 8 + j] = mm.data[j];
                }
                Application.DoEvents();
                
                if (Progress != null)
                    Progress(this, new MyEventArgs(i));
//                Application.DoEvents();
            }
            //mbuf.RemoveAt(0);
            //mbuf.RemoveRange(0, count);
            return true;
        }
        public Boolean SendCmd(ref canmsg_t msg, int timeout)
        {
            if (!Send(ref msg))
                return false;
            if (!Recv(ref msg, timeout))
                return false;
            return true;
        }
        public int GetStatus()
        {
            int result = 0;
            return result;
        }
        public void Recv_Enable()
        {
            Trace.WriteLine("Marathon Recv enable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
            flag_thr = true;
            thr = new Thread(t_recv);
            //thr.Priority = ThreadPriority.Highest;
            thr.Start();
            //thr.Priority = ThreadPriority.Highest;
        }
        public void Recv_Disable()
        {
            Trace.WriteLine("Marathon Recv disable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
            flag_thr = false;
            if (thr != null)
                if (thr.IsAlive)
                {
                    thr.Join();
                    thr.Abort();
                }
            thr = null;
        }
        ~MCANConverter()
        {
            if (Is_Open)
                Close();
        }
        void print_RX_TX(canmsg_t mm)
        {
            Trace.Write("ID=" + mm.id.ToString("X2") + " len=" + mm.len.ToString());
            Trace.Write(" Data:");
            for (int i = 0; i < mm.len; i++)
                Trace.Write(" 0x" + mm.data[i].ToString("X2"));
            Trace.WriteLine("");
        }
        public void t_recv()
        {
            canmsg_t mess = new canmsg_t();
            mess.data = new Byte[8];
            canwait_t cw = new canwait_t();
            canerrs_t ce = new canerrs_t();
            cw.chan = Port;
            cw.wflags = Const.CI_WAIT_RC | Const.CI_WAIT_ER;
            Int16 ret = 0;
            CiStart(0);
            CiErrsGetClear(Port, ref ce);
            while (flag_thr)
            {
                try
                {
                    CiErrsGetClear(Port, ref ce);
                    ret = CiWaitEvent(ref cw, 1, 1000);
                }
                catch (Exception)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не удалось принять сообщение. Err " + ret.ToString()));
                    Trace.WriteLine("CiWaitEvent() failed, errcode = " + ret.ToString());
                }
                if (ret == 0)
                {
                    Trace.WriteLine("thread timeout");
                    continue;
                }
                else if (ret > 0)
                {
                    if ((cw.rflags & Const.CI_WAIT_RC) > 0)
                    {
//                        do
//                        {
                            ret = CiRead(Port, ref mess, 1);
                            if (ret >= 1)
                            {
                                //Trace.Write("MCAN Recv packets: " + ret.ToString());
                                //print_RX_TX(msg);
                                push_back(mess);
                                //continue;
                            }
//                        } while (ret > 0);
                    }
                    if ((cw.rflags & Const.CI_WAIT_ER) > 0)
                    {
                        canerrs_t errs = new canerrs_t();
                        ret = CiErrsGetClear(Port, ref errs);
                        if (ret >= 0)
                        {
                            if (errs.ewl > 0)
                                Trace.WriteLine("EWL times = " + errs.ewl.ToString());
                            if (errs.boff > 0)
                                Trace.WriteLine("BOFF times " + errs.boff.ToString());
                            if (errs.hwovr > 0)
                                Trace.WriteLine("HOVR times " + errs.hwovr.ToString());
                            if (errs.swovr > 0)
                                Trace.WriteLine("SOVR times " + errs.swovr.ToString());
                            if (errs.wtout > 0)
                                Trace.WriteLine("WTOUT times " + errs.wtout.ToString());
                        }
                    }
                    continue;
                }
                else
                {
                    Trace.WriteLine("thread timeout");
//                    if (ErrEvent != null)
//                        ErrEvent(this, new MyEventArgs("Не удалось принять сообщение"));
//                    continue;
                }
            }
        }
        public void Clear_RX()
        {
            canmsg_t mess = new canmsg_t();
            mess.data = new Byte[8];
            Int16 ret = 0;
            do
            {
                ret = CiRead(Port, ref mess, 1);
            } while (ret > 0);
            if(mbuf.Count > 0)
                mbuf.Clear();
        }
        Boolean rx(ref canmsg_t msg, int timeout)
        {
            canwait_t cw = new canwait_t();
            canerrs_t ce = new canerrs_t();
            canmsg_t[] mmm = new canmsg_t[20000];
            cw.chan = Port;
            cw.wflags = Const.CI_WAIT_RC | Const.CI_WAIT_ER;
            Int16 ret = 0;
            CiStart(0);
            CiErrsGetClear(Port, ref ce);
            try
            {
                ret = CiWaitEvent(ref cw, 1, timeout);
            }
            catch (Exception)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось принять сообщение. Err " + ret.ToString()));
                Trace.WriteLine("CiWaitEvent() failed, errcode = " + ret.ToString());
            }
            if (ret < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось принять сообщение. Err " + ret.ToString()));
                Trace.WriteLine("CiWaitEvent() failed, errcode = " + ret.ToString());
                return false;
            }
            else if (ret > 0)
            {
                if ((cw.rflags & Const.CI_WAIT_RC) > 0)
                {
                    ret = CiRead(Port, ref msg, 1);
                    if (ret >= 1)
                    {
                        //Trace.Write("MCAN Recv packets: " + ret.ToString());
                        //print_RX_TX(msg);
                        return true;
                    }
                    else
                    {
                        Trace.WriteLine("error recieving frame from CAN, errcode = " + ret.ToString());
                        if (ErrEvent != null)
                            ErrEvent(this, new MyEventArgs("Не удалось принять сообщение"));
                        return false;
                    }
                }
                if ((cw.rflags & Const.CI_WAIT_ER) > 0)
                {
                    canerrs_t errs = new canerrs_t();
                    ret = CiErrsGetClear(Port, ref errs);
                    if (ret >= 0)
                    {
                        if (errs.ewl > 0)
                            Trace.WriteLine("EWL times = " + errs.ewl.ToString());
                        if (errs.boff > 0)
                            Trace.WriteLine("BOFF %d times " + errs.boff.ToString());
                        if (errs.hwovr > 0)
                            Trace.WriteLine("HOVR %d times " + errs.hwovr.ToString());
                        if (errs.swovr > 0)
                            Trace.WriteLine("SOVR %d times " + errs.swovr.ToString());
                        if (errs.wtout > 0)
                            Trace.WriteLine("WTOUT %d times " + errs.wtout.ToString());
                    }
                    else
                    {
                        Trace.WriteLine("CiErrsGetClear() failed");
                        if (ErrEvent != null)
                            ErrEvent(this, new MyEventArgs("Не удалось принять сообщение"));
                        return false;
                    }
                }
                return true;
            }
            else
            {
                Trace.WriteLine("CiWaitEvent timeout");
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось принять сообщение"));
                return false;
            }
        }
        public int VectorSize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        int vecsize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        void push_back(canmsg_t msg)
        {
            mtx.WaitOne();
            mbuf.Add(msg);
            mtx.ReleaseMutex();
        }
        Boolean pop(ref canmsg_t msg)
        {
            mtx.WaitOne();
            if (mbuf.Count >= 1)
            {
                msg = mbuf[0];
                mbuf.RemoveAt(0);
                mtx.ReleaseMutex();
                return true;
            }
            else
            {
                mtx.ReleaseMutex();
                return false;
            }
        }
        public void HWReset()
        {
            CiHwReset(0);
        }
    }    
    #endregion
    #region M2CANConverter
    public class M2CANConverter : IUCANConverter
    {
        public event MyDelegate ErrEvent;
        public event MyDelegate Progress;

        public canerrs_t errs = new canerrs_t();
        public canwait_t cw = new canwait_t();
        public canmsg_t frame = new canmsg_t();
        Boolean flag_thr = true;
        List<canmsg_t> mbuf = new List<canmsg_t>();
        Thread thr;
        Mutex mtx = new Mutex();
        /*
        EXPORT Int16 __stdcall MarCAN_Open(UInt16 speed);
        EXPORT Int16 __stdcall MarCAN_Close(void);
        EXPORT Int16 __stdcall MarCAN_SetCANSpeed(UInt16 speed);
        EXPORT Int16 __stdcall MarCAN_ClearRX(void);
        EXPORT Int16 __stdcall MarCAN_GetStatus(chipstat_t *Status);
        EXPORT Int16 __stdcall MarCAN_Write(pRX_TX_Buffer Buffer);
        EXPORT Int16 __stdcall MarCAN_GetErrorCounter(canerrs_t *Counter);
        EXPORT Int16 __stdcall MarCAN_HardReset(HANDLE Handle, int Channel);
        EXPORT UINT64 __stdcall MarCAN_GetAPIVer(void);
        EXPORT BYTE __stdcall MarCAN_GetByte(int num);
        EXPORT void __stdcall MarCAN_Recv_Enable(void);
        EXPORT void __stdcall MarCAN_Recv_Disable(void);
        EXPORT Int16 __stdcall MarCAN_Pop(pRX_TX_Buffer Buffer);
        EXPORT Int16 __stdcall MarCAN_VecSize(void);
        */
        #region Импорт функций из ДЛЛ
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_Open(UInt16 speed);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_Close();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_SetCANSpeed(UInt16 speed);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_ClearRX();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_GetStatus(ref chipstat_t Status);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_Write(ref canmsg_t Buffer);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_GetErrorCounter(ref canerrs_t Counter);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_HardReset();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt64 MarCAN_GetAPIVer();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Byte MarCAN_GetByte(int num);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void MarCAN_Recv_Enable();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void MarCAN_Recv_Disable();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_Pop(ref canmsg_t Buffer);
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_VecSize();
        [DllImport(@"marCAN.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern Int16 MarCAN_BoardInfo(ref canboard_t binfo);
        #endregion
        public M2CANConverter()
        {
            Port = 0;
            Speed = 0;
            Is_Open = false;
        }
        public String Info
        {
            set;
            get;
        }
        public Byte Port
        {
            set;
            get;
        }
        public Byte Speed
        {
            set;
            get;
        }
        public Boolean Is_Open
        {
            get;
            set;
        }
        public Boolean Is_Present
        {
            get
            {
                if (Open())
                {
                    Close();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set
            {
            }
        }
        public String GetAPIVer
        {
            get
            {
                UInt64 ver = MarCAN_GetAPIVer(); //#define VER 0x0000290120161346
                int a1 = (int)(ver >> 40);
                int a2 = (int)(ver >> 32) & 0xFF;
                int a3 = (int)(ver >> 16) & 0xFFFF;
                int a4 = (int)(ver >> 8) & 0xFF;
                int a5 = (int)(ver & 0xFF);

                return a1.ToString("X02") + "." + a2.ToString("X02") + "." + a3.ToString("X04") + " " + a4.ToString("X02") + ":" + a5.ToString("X02");
            }
            set { }
        }
        public Boolean Open()
        {
            try
            {
                if (MarCAN_Open(Speed) < 0)
                {
                    if (ErrEvent != null)
                        ErrEvent(this, new MyEventArgs("Не могу открыть CAN"));
                    Is_Open = false;
                    return false;
                }
            }
            catch (Exception)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не могу открыть CAN"));
                Is_Open = false;
                return false;
            }

            canboard_t binfo = new canboard_t();
            binfo.brdnum = 0;
            if (MarCAN_BoardInfo(ref binfo) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не подключен адаптер"));
                return false;
            }

            Info = new String(binfo.name, 3, 13) + " (" + new String(binfo.manufact, 3, 20) + ")";

            cw.chan = Port;
            cw.wflags = Const.CI_WAIT_RC | Const.CI_WAIT_ER;
            frame.data = new Byte[8];
            canerrs_t errs = new canerrs_t();
            MarCAN_GetErrorCounter(ref errs);
            Is_Open = true;
            return true;
        }
        public void Close()
        {
            if (thr != null)
                if (thr.IsAlive)
                    thr.Abort();
            thr = null;
            MarCAN_Close();
            Is_Open = false;
        }
        public Boolean Send(ref canmsg_t msg)
        {
            canerrs_t ce = new canerrs_t();
            MarCAN_GetErrorCounter(ref ce);

            if (MarCAN_Write(ref msg) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение"));
                return false;
            }
            else
            {
#if DDDM
                Trace.Write("M2 Send data: ");
                print_RX_TX(msg);
#endif
                return true;
            }
        }
        public Boolean Send(ref canmsg_t msg, int timeout)
        {
            canerrs_t ce = new canerrs_t();
            MarCAN_GetErrorCounter(ref ce);

            if (MarCAN_Write(ref msg) < 0)
            {
                if (ErrEvent != null)
                    ErrEvent(this, new MyEventArgs("Не удалось отправить сообщение"));
                return false;
            }
            else
            {
#if DDDM
                Trace.Write("M2 Send data: ");
                print_RX_TX(msg);
#endif
                return true;
            }
        }
        public Boolean Recv(ref canmsg_t msg, int timeout)
        {
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while (cnt < 1 && (Int32)timeout-- > 0);
            if ((Int32)timeout <= 0)
                return false;
            pop(ref msg);
#if DDDM
            Trace.Write("M2 Recv data: ");
            print_RX_TX(msg);
            Trace.WriteLine("");
#endif
            return true;
        }
        public Boolean RecvPack(ref Byte[] arr, ref int count, int timeout)
        {
            canmsg_t mm = new canmsg_t();
            mm.data = new Byte[8];
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while (cnt < count && timeout-- > 0);
#if DDDM
            Trace.WriteLine("M2CAN packets: " + cnt.ToString());
#endif
            if (timeout <= 0)
            {
                Trace.WriteLine("MCAN RecvPack Error timeout");
                //                return false;
            }
            for (int i = 0; i < count; i++)
            {
                //                Application.DoEvents();
                pop(ref mm);
                //                print_RX_TX(mm);
                for (int j = 0; j < mm.len; j++)
                {
                    if ((i * 8 + j) < arr.Length)
                        arr[i * 8 + j] = mm.data[j];
                }
                Application.DoEvents();

                if (Progress != null)
                    Progress(this, new MyEventArgs(i));
                //                Application.DoEvents();
            }
            //mbuf.RemoveAt(0);
            //mbuf.RemoveRange(0, count);
            return true;
        }
        public Boolean SendCmd(ref canmsg_t msg, int timeout)
        {
            if (!Send(ref msg))
                return false;
            if (!Recv(ref msg, timeout))
                return false;
            return true;
        }
        public int GetStatus()
        {
            int result = 0;
            return result;
        }
        public void Recv_Enable()
        {
            Trace.WriteLine("Marathon2 Recv enable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
            MarCAN_Recv_Enable();
        }
        public void Recv_Disable()
        {
            Trace.WriteLine("Marathon Recv disable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
            MarCAN_Recv_Disable();
        }
        ~M2CANConverter()
        {
            if (Is_Open)
                Close();
        }
        void print_RX_TX(canmsg_t mm)
        {
            Trace.Write("ID=" + mm.id.ToString("X2") + " len=" + mm.len.ToString());
            Trace.Write(" Data:");
            for (int i = 0; i < mm.len; i++)
                Trace.Write(" 0x" + mm.data[i].ToString("X2"));
            Trace.WriteLine("");
        }
        public void Clear_RX()
        {
            MarCAN_ClearRX();
            if (mbuf.Count > 0)
                mbuf.Clear();
        }
        public int VectorSize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        int vecsize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        void push_back(canmsg_t msg)
        {
            mtx.WaitOne();
            mbuf.Add(msg);
            mtx.ReleaseMutex();
        }
        Boolean pop(ref canmsg_t msg)
        {
            mtx.WaitOne();
            if (mbuf.Count >= 1)
            {
                msg = mbuf[0];
                mbuf.RemoveAt(0);
                mtx.ReleaseMutex();
                return true;
            }
            else
            {
                mtx.ReleaseMutex();
                return false;
            }
        }
        public void HWReset()
        {
            MarCAN_HardReset();
        }
    }
    #endregion

    #region FCANConverter
    public class FCANConverter : IUCANConverter
    {
        public event MyDelegate ErrEvent;
        public event MyDelegate Progress;

        public canerrs_t errs = new canerrs_t();
        public canwait_t cw = new canwait_t();
        public canmsg_t frame = new canmsg_t();
        Boolean flag_thr = true;
        List<canmsg_t> mbuf = new List<canmsg_t>();
        Thread thr;
        Mutex mtx = new Mutex();
        public FCANConverter()
        {
            Port = 0;
            Speed = 0;
            Is_Open = false;
        }
        public String Info
        {
            set;
            get;
        }
        public Byte Port
        {
            set;
            get;
        }
        public Byte Speed
        {
            set;
            get;
        }
        public Boolean Is_Open
        {
            get;
            set;
        }
        public Boolean Is_Present
        {
            get
            {
                if (Open())
                {
                    Close();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set
            {
            }
        }
        public String GetAPIVer
        {
            get
            {
//                UInt64 ver = //#define VER 0x0000290120261746
                UInt64 ver = 0x0000011220162136;
                int a1 = (int)(ver >> 40);
                int a2 = (int)(ver >> 32) & 0xFF;
                int a3 = (int)(ver >> 16) & 0xFFFF;
                int a4 = (int)(ver >> 8) & 0xFF;
                int a5 = (int)(ver & 0xFF);

                return a1.ToString("X02") + "." + a2.ToString("X02") + "." + a3.ToString("X04") + " " + a4.ToString("X02") + ":" + a5.ToString("X02");
            }
            set { }
        }
        public Boolean Open()
        {
            Info = "Fake CAN driver";
            Is_Open = true;
            return true;
        }
        public void Close()
        {
            if (thr != null)
                if (thr.IsAlive)
                    thr.Abort();
            thr = null;
            Is_Open = false;
        }
        public Boolean Send(ref canmsg_t msg)
        {
            canerrs_t ce = new canerrs_t();
            return true;
        }
        public Boolean Send(ref canmsg_t msg, int timeout)
        {
            canerrs_t ce = new canerrs_t();
            return true;
        }
        public Boolean Recv(ref canmsg_t msg, int timeout)
        {
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while (cnt < 1 && (Int32)timeout-- > 0);
            if ((Int32)timeout <= 0)
                return false;
            pop(ref msg);
#if DDDM
            Trace.Write("M2 Recv data: ");
            print_RX_TX(msg);
            Trace.WriteLine("");
#endif
            return true;
        }
        public Boolean RecvPack(ref Byte[] arr, ref int count, int timeout)
        {
            canmsg_t mm = new canmsg_t();
            mm.data = new Byte[8];
            int cnt = 0;
            do
            {
                cnt = vecsize();
                Thread.Sleep(1);
            } while (cnt < count && timeout-- > 0);
#if DDDM
            Trace.WriteLine("M2CAN packets: " + cnt.ToString());
#endif
            if (timeout <= 0)
            {
                Trace.WriteLine("MCAN RecvPack Error timeout");
                //                return false;
            }
            for (int i = 0; i < count; i++)
            {
                //                Application.DoEvents();
                pop(ref mm);
                //                print_RX_TX(mm);
                for (int j = 0; j < mm.len; j++)
                {
                    if ((i * 8 + j) < arr.Length)
                        arr[i * 8 + j] = mm.data[j];
                }
                Application.DoEvents();

                if (Progress != null)
                    Progress(this, new MyEventArgs(i));
                //                Application.DoEvents();
            }
            //mbuf.RemoveAt(0);
            //mbuf.RemoveRange(0, count);
            return true;
        }
        public Boolean SendCmd(ref canmsg_t msg, int timeout)
        {
            if (!Send(ref msg))
                return false;
            if (!Recv(ref msg, timeout))
                return false;
            return true;
        }
        public int GetStatus()
        {
            int result = 0;
            return result;
        }
        public void Recv_Enable()
        {
            Trace.WriteLine("FakeCAN Recv enable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
//            MarCAN_Recv_Enable();
        }
        public void Recv_Disable()
        {
            Trace.WriteLine("Marathon Recv disable");
            if (vecsize() > 0)
            {
                mtx.WaitOne();
                mbuf.Clear();
                mtx.ReleaseMutex();
            }
//            MarCAN_Recv_Disable();
        }
        ~FCANConverter()
        {
            if (Is_Open)
                Close();
        }
        void print_RX_TX(canmsg_t mm)
        {
            Trace.Write("ID=" + mm.id.ToString("X2") + " len=" + mm.len.ToString());
            Trace.Write(" Data:");
            for (int i = 0; i < mm.len; i++)
                Trace.Write(" 0x" + mm.data[i].ToString("X2"));
            Trace.WriteLine("");
        }
        public void Clear_RX()
        {
//            MarCAN_ClearRX();
            if (mbuf.Count > 0)
                mbuf.Clear();
        }
        public int VectorSize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        int vecsize()
        {
            mtx.WaitOne();
            int ii = mbuf.Count;
            mtx.ReleaseMutex();
            return ii;
        }
        void push_back(canmsg_t msg)
        {
            mtx.WaitOne();
            mbuf.Add(msg);
            mtx.ReleaseMutex();
        }
        Boolean pop(ref canmsg_t msg)
        {
            mtx.WaitOne();
            if (mbuf.Count >= 1)
            {
                msg = mbuf[0];
                mbuf.RemoveAt(0);
                mtx.ReleaseMutex();
                return true;
            }
            else
            {
                mtx.ReleaseMutex();
                return false;
            }
        }
        public void HWReset()
        {
//            MarCAN_HardReset();
        }
    }
    #endregion


    #region IniFile
    public class IniFile
    {
        [DllImport("kernel32.dll")]
        private extern static int GetPrivateProfileString(String AppName, String KeyName, String Default, StringBuilder ReturnedString, UInt32 Size, String FileName);
        [DllImport("kernel32.dll")]
        private extern static int WritePrivateProfileString(String AppName, String KeyName, String Str, String FileName);
        public IniFile(string filename)
        {
            IniFileName = filename;
        }
        public String IniFileName
        {
            get;
            set;
        }
        public String _GetString(String section, String key)
        {
            StringBuilder s1 = new StringBuilder(128);
            GetPrivateProfileString(section, key, "", s1, 128, IniFileName);
            return s1.ToString();
        }
        public Int64 _GetInt(String section, String key)
        {
            StringBuilder s1 = new StringBuilder(100);
            GetPrivateProfileString(section, key, "", s1, 100, IniFileName);
            return Int64.Parse(s1.ToString());
        }
        public Double _GetDouble(String section, String key)
        {
            StringBuilder s1 = new StringBuilder(100);
            GetPrivateProfileString(section, key, "", s1, 100, IniFileName);
            return Double.Parse(s1.ToString());
        }
        public void _SetString(String section, String key, String val)
        {
            WritePrivateProfileString(section, key, val, IniFileName);
        }
        public void _SetInt(String section, String key, Int64 val)
        {
            WritePrivateProfileString(section, key, val.ToString(), IniFileName);
        }
        public void _SetDouble(String section, String key, Double val)
        {
            WritePrivateProfileString(section, key, val.ToString(), IniFileName);
        }
    }

    #endregion
    #region Const
    public class Const
    {
//        public const Byte OLO_Left = 0x11;
//        public const Byte OLO_Right = 0x12;
        public const Byte OLO_Left = 0x12;
        public const Byte OLO_Right = 0x11;
        public const Byte OLO_All = 0x00;

        public const byte CIO_BLOCK = 0x1;         // ignored (block mode was removed in CHAI 2.x
        public const byte CIO_CAN11 = 0x2;
        public const byte CIO_CAN29 = 0x4;

        //Flags_for_CiWaitEvent
        public const byte CI_WAIT_RC = 0x1;
        public const byte CI_WAIT_TR = 0x2;
        public const byte CI_WAIT_ER = 0x4;

        //Commands_for_CiSetLom
        public const byte CI_LOM_OFF = 0;
        public const byte CI_LOM_ON = 1;

        public const byte CI_CMD_GET = 0;
        public const byte CI_CMD_SET = 1;

        public const byte CI_OFF = 0;
        public const byte CI_ON = 1;

        //Transmit_status
        public const byte CI_TR_COMPLETE_OK = 0x0;
        public const byte CI_TR_COMPLETE_ABORT = 0x1;
        public const byte CI_TR_INCOMPLETE = 0x2;
        public const byte CI_TR_DELAY = 0x3;

        //Transmit_cancel_status
        public const byte CI_TRCANCEL_TRANSMITTED = 0;
        public const byte CI_TRCANCEL_ABORTED = 1;
        public const byte CI_TRCANCEL_NOTRANSMISSION = 2;
        public const byte CI_TRCANCEL_DELAYABORTED = 3;

        //Bits in canmsg_t.flags field
        public const byte MSG_RTR = 0;
        public const byte MSG_FF = 2;              /* if set - extended frame format is used */
        public const byte FRAME_RTR = 1;
        public const byte FRAME_EFF = 4;
        public const byte FRAME_TRDELAY = 0x10;

        //Error_codes
        public const byte ECIOK = 0;            /* success */
        public const byte ECIGEN = 1;            /* generic (not specified) error */
        public const byte ECIBUSY = 2;            /* device or resourse busy */
        public const byte ECIMFAULT = 3;            /* memory fault */
        public const byte ECISTATE = 4;            /* function can't be called for chip in current state */
        public const byte ECIINCALL = 5;            /* invalid call; function can't be called for this object */
        public const byte ECIINVAL = 6;            /* invalid parameter */
        public const byte ECIACCES = 7;            /* can not access resource */
        public const byte ECINOSYS = 8;            /* function or feature not implemented */
        public const byte ECIIO = 9;            /* input/output error */
        public const byte ECINODEV = 10;           /* no such device or object */
        public const byte ECIINTR = 11;           /* call was interrupted by event */
        public const byte ECINORES = 12;           /* no resources */
        public const byte ECITOUT = 13;            /* time out occured */

        //CAN_Events
        public const byte CIEV_RC = 1;
        public const byte CIEV_TR = 2;
        public const byte CIEV_CANERR = 6;
        public const byte CIEV_EWL = 3;
        public const byte CIEV_BOFF = 4;
        public const byte CIEV_HOVR = 5;
        public const byte CIEV_WTOUT = 7;
        public const byte CIEV_SOVR = 8;

        //CAN_controller_types
        public const byte CHIP_UNKNOWN = 0;
        public const byte SJA1000 = 1;
        public const byte EMU = 2;
        public const byte MSCAN = 3;

        //Manufacturers
        public const byte MANUF_UNKNOWN = 0;
        public const byte MARATHON = 1;
        public const byte SA = 2;
        public const byte FREESCALE = 3;

        //CAN_adapter_types
        public const byte BRD_UNKNOWN = 0;
        public const byte CAN_BUS_ISA = 1;
        public const byte CAN_BUS_MICROPC = 2;
        public const byte CAN_BUS_PCI = 3;
        public const byte CAN_EMU = 4;
        public const byte CAN2_PCI_M = 5;
        public const byte MPC5200TQM = 6;
        public const byte CAN_BUS_USB = 7;
        public const byte CAN_BUS_PCI_E = 8;
        public const byte CAN_BUS_USB_NP = 9;
        public const byte CAN_BUS_USB_NPS = 10;

        public const _u16 IMAGE_CX = 319;
        public const _u16 IMAGE_CY = 255;

        public const byte CAN_ID_TEST_PLIS = 0x40;
        public const byte CAN_ID_TEST_RAM = 0x41;
        public const byte CAN_ID_TEST_FLASH = 0x42;
        public const byte CAN_ID_GET_CMOS1_IMAGE = 0x43;
        public const byte CAN_ID_GET_CMOS2_IMAGE = 0x44;
        public const byte CAN_ID_SET_PELTIE1 = 0x45;
        public const byte CAN_ID_SET_PELTIE2 = 0x46;
        public const byte CAN_ID_GET_TEMPERATURES = 0x47;
        public const byte CAN_ID_RUN_GENERATOR = 0x48;
        public const byte CAN_ID_STOP_GENERATOR = 0x49;

        public const byte CAN_ID_TEST_RAM_D21 = 0x50;
        public const byte CAN_ID_TEST_RAM_D13 = 0x51;
        public const byte CAN_ID_TEST_RAM_D19 = 0x52;
        public const byte CAN_ID_TEST_RAM_D13_2 = 0x59;
        public const byte CAN_ID_TEST_RAM_D19_2 = 0x5A;
        public const byte CAN_ID_TEST_RAM_D13_2_OUT = 0xB3;
        public const byte CAN_ID_TEST_RAM_D19_2_OUT = 0xB4;

        public const byte CAN_ID_TEST_RAM_D21_RUN = 0x53;
        public const byte CAN_ID_TEST_RAM_D13_RUN = 0x54;
        public const byte CAN_ID_TEST_RAM_D19_RUN = 0x55;

        public const byte CAN_ID_TEST_RAM_D21_STOP = 0x56;
        public const byte CAN_ID_TEST_RAM_D13_STOP = 0x57;
        public const byte CAN_ID_TEST_RAM_D19_STOP = 0x58;

        public const byte CAN_ID_RWTEST_RAM_D21 = 0x60;

        public const byte CAN_ID_GET2_CMOS1_IMAGE = 0x63;
        public const byte CAN_ID_GET2_CMOS2_IMAGE = 0x64;

        // New tests bus
        public const byte CAN_ID_TEST_RAM_D21_E = 0x5B;
        public const byte CAN_ID_TEST_RAM_D21_D = 0x5C;
        public const byte CAN_ID_TEST_RAM_D13_E = 0x5D;
        public const byte CAN_ID_TEST_RAM_D13_D = 0x5E;
        public const byte CAN_ID_TEST_RAM_D19_E = 0x5F;
        public const byte CAN_ID_TEST_RAM_D19_D = 0x65;
        public const byte CAN_ID_RESET = 0x00;


        public const byte COMMAND_CMOS1_SET_VREF = 0x01;
        public const byte COMMAND_CMOS1_SET_VINB = 0x02;
        public const byte COMMAND_CMOS1_ENABLE_TERMOSTAT = 0x03;
        public const byte COMMAND_CMOS1_GET_TEMPERATURE = 0x04;
        public const byte COMMAND_CMOS1_GET_RAW_IMAGE = 0x05;
        public const byte COMMAND_CMOS1_SAVE_BAD_PIXELS = 0x06;
        public const byte COMMAND_CMOS1_SAVE_CONFIG = 0x07;
        public const byte COMMAND_CMOS1_GET_BAD_PIXELS = 0x08;
        public const byte COMMAND_CMOS1_GET_CONFIG = 0x09;
        public const byte COMMAND_CMOS2_SET_VREF = 0x0a;
        public const byte COMMAND_CMOS2_SET_VINB = 0x0b;
        public const byte COMMAND_CMOS2_ENABLE_TERMOSTAT = 0x0c;
        public const byte COMMAND_CMOS2_GET_TEMPERATURE = 0x0d;
        public const byte COMMAND_CMOS2_GET_RAW_IMAGE = 0x0e;
        public const byte COMMAND_CMOS2_SAVE_BAD_PIXELS = 0x0f;
        public const byte COMMAND_CMOS2_SAVE_CONFIG = 0x10;
        public const byte COMMAND_CMOS2_GET_BAD_PIXELS = 0x11;
        public const byte COMMAND_CMOS2_GET_CONFIG = 0x12;
        //---------------------------------------------------------------------------
        public const byte COMMAND_CMOS_SET_SIMULATION_MODE = 0x80;
        public const byte COMMAND_FORMAT_FLASH = 0x81;
        //---------------------------------------------------------------------------
        public const byte STATUS_OK = 0x00;
        public const byte STATUS_ERROR = 0x01;
        //---------------------------------------------------------------------------
        public const byte CAN_PC2ARM_MSG_ID = 0x30;
        public const byte CAN_ARM2PC_MSG_ID = 0x31;
        public const byte CAN_MAX_DATA_SIZE = 8;
        //---------------------------------------------------------------------------
        public const byte MAGIC_BYTE = 0x55;

        //---------------------------------------------------------------------------
        public const int BAD_PIXELS_ARRAY_SIZE = 256;
        public const int SN_LENGTH = 128;

        // common definitions
        public const byte CAN_MAX_PACKET_SIZE = 8;		// bytes
        public const byte PACKETS_IN_BLOCK = 8;		// num. of packets in block before acknowledge packet
        //----------------------------------------------------------------------------
        public const _u8 COMMAND_UPLOAD_FIRMWARE = 0x01;
        public const _u8 COMMAND_EXECUTE_USER_CODE = 0x02;

        // error codes
        public const byte CMD_ERR_NO_ERROR = 0x00;
        public const byte CMD_ERR_INVALID_FIRMWARE_SIZE = 0x01;
        public const byte CMD_ERR_IAP_ERROR = 0x02;
        public const byte CMD_ERR_USER_CODE_NOT_PRESENT = 0x03;
        public const byte CMD_ERR_CRC8_ERROR = 0x04;

        // extended error codes (for CMD_ERR_IAP_ERROR)
        public const byte IAP_ERR_CMD_SUCCESS = 0;
        public const byte IAP_ERR_INVALID_COMMAND = 1;
        public const byte IAP_ERR_SRC_ADDR_ERROR = 2;
        public const byte IAP_ERR_DST_ADDR_ERROR = 3;
        public const byte IAP_ERR_SRC_ADDR_NOT_MAPPED = 4;
        public const byte IAP_ERR_DST_ADDR_NOT_MAPPED = 5;
        public const byte IAP_ERR_COUNT_ERROR = 6;
        public const byte IAP_ERR_INVALID_SECTOR = 7;
        public const byte IAP_ERR_SECTOR_NOT_BLANK = 8;
        public const byte IAP_ERR_SECTOR_NOT_PREPARED_FOR_WRITE_OPERATION = 9;
        public const byte IAP_ERR_COMPARE_ERROR = 10;
        public const byte IAP_ERR_BUSY = 11;

        public const _u16 CAN_MSG_ID_PC2MC = 0x030;
        public const _u16 CAN_MSG_ID_MC2PC = 0x031;

        public const _u16 FLAG_ERASE_USER_CODE = 0x01;
    }

    #endregion
    #region CANLib
    public class CANLib
    {
        #region Методы доступа к атрибутам сборки

        public static string AssemblyTitle
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
                if (attributes.Length > 0)
                {
                    AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
                    if (titleAttribute.Title != "")
                    {
                        return titleAttribute.Title;
                    }
                }
                return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
            }
        }

        public static string AssemblyVersion
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        public static string AssemblyDescription
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyDescriptionAttribute)attributes[0]).Description;
            }
        }

        public static string AssemblyProduct
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyProductAttribute)attributes[0]).Product;
            }
        }

        public static string AssemblyCopyright
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
            }
        }

        public static string AssemblyCompany
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCompanyAttribute)attributes[0]).Company;
            }
        }
        #endregion
    }

    #endregion
    #region Структуры данных
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct canmsg_t
    {
        public UInt32 id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public Byte[] data;
        public Byte len;
        public UInt16 flags;            /* bit 0 - RTR, 2 - EFF */
        public UInt32 ts;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct canwait_t
    {
        public Byte chan;
        public Byte wflags;
        public Byte rflags;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct canboard_t
    {
        public Byte brdnum;
        public UInt32 hwver;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public Int16[] chip;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] name;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] manufact;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct canerrs_t
    {
        public UInt16 ewl;
        public UInt16 boff;
        public UInt16 hwovr;
        public UInt16 swovr;
        public UInt16 wtout;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct chipstat_t
    {
        public Int16 type;
        public Int16 brdnum;
        public Int32 irq;
        public UInt32 baddr;
        public Byte state;
        public UInt32 hovr_cnt;
        public UInt32 sovr_cnt;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] _pad;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct sja1000stat_t
    {
        public Int16 type;
        public Int16 brdnum;
        public Int32 irq;
        public UInt32 baddr;
        public Byte state;
        public UInt32 hovr_cnt;
        public UInt32 sovr_cnt;
        public Byte mode;
        public Byte stat;
        public Byte inten;
        public Byte clkdiv;
        public Byte ecc;
        public Byte ewl;
        public Byte rxec;
        public Byte txec;
        public Byte rxmc;
        public UInt32 acode;
        public UInt32 amask;
        public Byte btr0;
        public Byte btr1;
        public Byte outctl;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public char[] _pad;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct chstat_desc_t
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public Byte[,] name;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public Byte[,] val;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct _bytes
    {
        public Byte lo_byte;
        public Byte hi_byte;
    };

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public unsafe struct WORD_UNION
    {
        [FieldOffset(0)]
        public ushort word;
        [FieldOffset(0)]
        public _bytes bytes;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct _words
    {
        public WORD_UNION lo_word;
        public WORD_UNION hi_word;
    };

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public unsafe struct DWORD_UNION
    {
        [FieldOffset(0)]
        public UInt32 dword;
        [FieldOffset(0)]
        public Single fvalue;
        [FieldOffset(0)]
        public _words words;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct COMMAND
    {
        public Byte magic;
        public Byte cmd;
        public DWORD_UNION prm;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct RESULT
    {
        public Byte magic;
        public Byte stat;
        public DWORD_UNION prm;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct FIFO_ITEM
    {
        public UInt16 x;
        public UInt16 y;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct BAD_PIX_FILE_STRUCTURE
    {
        public UInt16 count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public FIFO_ITEM[] array;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CONFIG_FILE_STRUCTURE
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public Byte[] szSerialNumber; // matrix serial number, zero-terminated string
        public UInt16 nXc, nYc;			// centers of matrices, pix
        public UInt16 nRs, nRb;			// lens field of view radius, pix
        public Byte cLc;					// neighbour distance for clasters, pix
        public UInt16 nLimit;				// FIFO limit (10-bit)
        public UInt16 nVinb, nVref;		// VINB & VREF (10-bit)
        public Single fA, fB, fC;			// Fi=A*x^2 + Bx + C
    };
    #endregion

    #region Создание ярлыка
    static class ShellLink
    {
        [ComImport,
        Guid("000214F9-0000-0000-C000-000000000046"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            [PreserveSig]
            int GetPath(
                [Out, MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszFile,
                int cch, ref IntPtr pfd, uint fFlags);

            [PreserveSig]
            int GetIDList(out IntPtr ppidl);

            [PreserveSig]
            int SetIDList(IntPtr pidl);

            [PreserveSig]
            int GetDescription(
                [Out, MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszName, int cch);

            [PreserveSig]
            int SetDescription(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszName);

            [PreserveSig]
            int GetWorkingDirectory(
                [Out, MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszDir, int cch);

            [PreserveSig]
            int SetWorkingDirectory(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszDir);

            [PreserveSig]
            int GetArguments(
                [Out, MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszArgs, int cch);

            [PreserveSig]
            int SetArguments(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszArgs);

            [PreserveSig]
            int GetHotkey(out ushort pwHotkey);

            [PreserveSig]
            int SetHotkey(ushort wHotkey);

            [PreserveSig]
            int GetShowCmd(out int piShowCmd);

            [PreserveSig]
            int SetShowCmd(int iShowCmd);

            [PreserveSig]
            int GetIconLocation(
                [Out, MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszIconPath, int cch, out int piIcon);

            [PreserveSig]
            int SetIconLocation(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszIconPath, int iIcon);

            [PreserveSig]
            int SetRelativePath(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszPathRel, uint dwReserved);

            [PreserveSig]
            int Resolve(IntPtr hwnd, uint fFlags);

            [PreserveSig]
            int SetPath(
                [MarshalAs(UnmanagedType.LPWStr)]
                string pszFile);
        }

        [ComImport,
        Guid("00021401-0000-0000-C000-000000000046"),
        ClassInterface(ClassInterfaceType.None)]
        private class shl_link { }

        internal static IShellLinkW CreateShellLink()
        {
            return (IShellLinkW)(new shl_link());
        }
    }

    public static class ShortCut
    {
        public static void Create(
            string PathToFile, string PathToLink,
            string Arguments, string Description)
        {
            ShellLink.IShellLinkW shlLink = ShellLink.CreateShellLink();

            Marshal.ThrowExceptionForHR(shlLink.SetDescription(Description));
            Marshal.ThrowExceptionForHR(shlLink.SetPath(PathToFile));
            Marshal.ThrowExceptionForHR(shlLink.SetArguments(Arguments));
            Marshal.ThrowExceptionForHR(shlLink.SetIconLocation(PathToFile, 0));
            Marshal.ThrowExceptionForHR(shlLink.SetWorkingDirectory(Path.GetDirectoryName(PathToFile)));

            ((System.Runtime.InteropServices.ComTypes.IPersistFile)shlLink).Save(PathToLink, false);
        }
        //public static void Create2(string name, string param, string icon, string desc, string hotkey)
        //{
        //    var shell = new WshShell();
        //    string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        //        name + ".lnk");
        //    var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
        //    shortcut.TargetPath = Application.ExecutablePath.ToLower() + param;
        //    shortcut.IconLocation = icon;
        //    if (desc == null) throw new ArgumentNullException("");
        //    shortcut.Description = desc;
        //    shortcut.Hotkey = hotkey;
        //    shortcut.Save();

        //    MessageBox.Show(shortcut.TargetPath);
        //}
    }

    #endregion

    public class msg_t
    {
        public const Byte mID_RESET = 0x01;
        public const Byte mID_MODULE = 0x06;
        public const Byte mID_SOER = 0x07;
        public const Byte mID_PROG = 0x03;
        public const Byte mID_SYNCTIME = 0x02;
        public const Byte mID_STATREQ = 0x04;
        public const Byte mID_STATUS = 0x05;
        public const Byte mID_DATA = 0x2D;
        public const Byte mID_SIMRESET = 0x08;
        public const Byte mID_GETTIME = 0x09;
        public const Byte mID_REQTIME = 0x0A;
        public const Byte mID_REQVER = 0x0B;

        public Byte messageID;
        public Byte deviceID;
        public Byte messageLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public Byte[] messageData;
        public msg_t()
        {
            messageData = new Byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            messageID = 0;
            deviceID = 0;
            messageLen = 0;
        }
        public msg_t FromCAN(canmsg_t msg)
        {
            msg_t tmp = new msg_t();
            tmp.messageData = msg.data;
            tmp.messageLen = msg.len;
            tmp.deviceID = (Byte)(msg.id & 0x1F);
            tmp.messageID = (Byte)(msg.id >> 5);
            return tmp;
        }
        public canmsg_t ToCAN(msg_t msg)
        {
            canmsg_t tmp = new canmsg_t();
            tmp.data = new Byte[8];
            tmp.data = msg.messageData;
            tmp.len = msg.messageLen;
            tmp.id = (uint)(msg.deviceID | (msg.messageID << 5));
            return tmp;
        }
    }

