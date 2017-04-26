using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;

namespace Ssh_Terminal
    {
    public partial class Form1 : Form
        {
        Chilkat.Ssh m_ssh = null;

        private int SshChannel { get; set; }

        // The current ongoing task will be either a read or send.
        // Send tasks are only started when key presses are ready to send,
        // thus a send task finishes quickly.  When it finishes, a new read
        // task will be started unless there are more key presses to send.
        // If a key press occurs, and a read task is ongoing, then it is cancelled
        // an the send task is started.
        // 
        // Note: The m_currentTask is used in both the foreground and background threads (in TaskFinished),
        // so always use Monitor.Enter(m_currentTask) and Monitor.Exit(m_currentTask)
        // Never let m_currentTask be null.  Instead, check for a task.Status equal to "empty".
        Chilkat.Task m_currentTask = new Chilkat.Task();

        private Chilkat.StringBuilder m_accumulatedKeyPresses = new Chilkat.StringBuilder();

        public Form1()
            {
            InitializeComponent();
            }

        private void Form1_Load(object sender, EventArgs e)
            {
            Chilkat.Global glob = new Chilkat.Global();
            bool success = glob.UnlockBundle("Anything for 30-day trial");
            if (!success)
                {
                MessageBox.Show("Chilkat trial expired.");
                }

            }

        // Connect to an SSH server and login.
        private void button1_Click(object sender, EventArgs e)
            {
            m_ssh = new Chilkat.Ssh();

            bool success = m_ssh.Connect(txtHost.Text, 22);
            if (!success)
                {
                textBox1.Text = m_ssh.LastErrorText;
                return;
                }

            success = m_ssh.AuthenticatePw(txtUsername.Text, txtPassword.Text);
            if (!success)
                {
                textBox1.Text = m_ssh.LastErrorText;
                return;
                }

            // Use QuickShell instead of the chunk of commented-out code below..
            SshChannel = m_ssh.QuickShell();
            if (SshChannel < 0)
                {
                textBox1.Text = m_ssh.LastErrorText;
                return;
                }

            //SshChannel = m_ssh.OpenSessionChannel();
            //if (SshChannel < 0)
            //    {
            //    textBox1.Text = m_ssh.LastErrorText;
            //    return;
            //    }
            //success = m_ssh.SendReqPty(SshChannel, "dumb", 80, 24, 0, 0);
            //if (!success)
            //    {
            //    textBox1.Text = m_ssh.LastErrorText;
            //    return;
            //    }
            //success = m_ssh.SendReqShell(SshChannel);
            //if (!success)
            //    {
            //    textBox1.Text = m_ssh.LastErrorText;
            //    return;
            //    }


            // Begin reading the channel  asynchronously and emit whatever arrives
            // to textBox1.

            m_ssh.OnTaskCompleted += m_ssh_OnTaskCompleted;

            Monitor.Enter(m_currentTask);
            Chilkat.Task t = m_currentTask;
            m_currentTask = m_ssh.ChannelReadAsync(SshChannel);
            if (m_currentTask != null)
                {
                m_currentTask.UserData = "read";
                m_currentTask.Run();
                }
            else
                {
                // Don't let m_currentTask be null
                m_currentTask = new Chilkat.Task();
                }
            Monitor.Exit(t);
            }

        void appendToTxtDebug(string s)
            {
            txtDebug.AppendText(s);
            txtDebug.SelectionStart = txtDebug.Text.Length;
            txtDebug.ScrollToCaret();
            }

        void bgAppendToTxtDebug(string s)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    txtDebug.AppendText(s);
                    txtDebug.SelectionStart = txtDebug.Text.Length;
                    txtDebug.ScrollToCaret();
                });
            }

        void handleChannelReadCompleted(Chilkat.TaskCompletedEventArgs args)
            {
            int result = args.Task.GetResultInt();

            string s = null;
            bool bStartNextTask = false;

            if (result == -1)
                {
                // Some error occurred..
                s = m_ssh.LastErrorText;
                }
            else if (result == -2)
                {
                // No additional data arrived, just fall through to start a send or read.
                bStartNextTask = true;
                }
            else if (result == 0)
                {
                // Check to see if the channel was closed or an EOF arrived..
                if (m_ssh.ChannelReceivedClose(SshChannel))
                    {
                    s = "\r\n(SSH channel closed)\r\n";
                    }
                else if (m_ssh.ChannelReceivedEof(SshChannel))
                    {
                    s = "\r\n(Received EOF on SSH channel)\r\n";
                    }
                else
                    {
                    // Don't expect to get here.. just fall through and start another task.
                    bStartNextTask = true;
                    }
                }
            else if (result > 0)
                {
                // Data arrived..
                s = m_ssh.GetReceivedText(SshChannel, "utf-8");
                bStartNextTask = true;
                }

            // We're in a background thread.  We must update the textbox 
            // in this way:
            if (s != null)
                {
                this.Invoke((MethodInvoker)delegate
                {
                    textBox1.AppendText(s); 
                    textBox1.SelectionStart = textBox1.Text.Length;
                    textBox1.ScrollToCaret();
                });
                }

            Monitor.Enter(m_currentTask);
            Chilkat.Task t = m_currentTask;
            m_currentTask = new Chilkat.Task();
            Monitor.Exit(t);

            if (bStartNextTask)
                {
                startNextTask();
                }
            }

        void startNextTask()
            {
            if (m_ssh == null) return;

            // Start a send task if key presses are waiting to be sent,
            // otherwise start another read task.
            if (m_accumulatedKeyPresses.Length > 0)
                {
                string s = m_accumulatedKeyPresses.GetAsString();

                // Log the string that we are sending as quoted-printable, so we can see the exact bytes.
                bgAppendToTxtDebug("Sent " + m_accumulatedKeyPresses.GetEncoded("qp", "utf-8") + "\r\n");

                m_accumulatedKeyPresses.Clear();

                Monitor.Enter(m_currentTask);
                Chilkat.Task t = m_currentTask;
                m_currentTask = m_ssh.ChannelSendStringAsync(SshChannel, s, "utf-8");
                if (m_currentTask != null)
                    {
                    m_currentTask.UserData = "send";
                    m_currentTask.Run();
                    }
                else
                    {
                    // Don't let m_currentTask be null
                    m_currentTask = new Chilkat.Task();
                    }
                Monitor.Exit(t);
                }
            else
                {
                Monitor.Enter(m_currentTask);
                Chilkat.Task t = m_currentTask;
                m_currentTask = m_ssh.ChannelReadAsync(SshChannel);
                if (m_currentTask != null)
                    {
                    m_currentTask.UserData = "read";
                    m_currentTask.Run();
                    }
                else
                    {
                    // Don't let m_currentTask be null
                    m_currentTask = new Chilkat.Task();
                    }
                Monitor.Exit(t);
                }
            }

        void handleChannelSendCompleted(Chilkat.TaskCompletedEventArgs args)
            {
            string s = null;
            bool bStartNextTask = true;

            // Get the return value of the ChannelSendString (which returns a boolean)
            bool success = args.Task.GetResultBool();
            if (!success)
                {
                // Failed to send the string..
                s = m_ssh.LastErrorText;
                bStartNextTask = false;
                }

            // We're in a background thread.  We must update the textbox 
            // in this way:
            if (s != null)
                {
                this.Invoke((MethodInvoker)delegate
                {
                    textBox1.AppendText(s);
                    textBox1.SelectionStart = textBox1.Text.Length;
                    textBox1.ScrollToCaret();
                });
                }

            Monitor.Enter(m_currentTask);
            Chilkat.Task t = m_currentTask;
            m_currentTask = new Chilkat.Task();
            Monitor.Exit(t);
            if (bStartNextTask)
                {
                startNextTask();
                }

            }

        // Check to see what task completed and route to the correct handler..
        void m_ssh_OnTaskCompleted(object sender, Chilkat.TaskCompletedEventArgs args)
            {
            // If this task was canceled, then just start the next task.
            // This would typically happen when a key is pressed.  A read task will be ongoing, and the key press handler
            // will cancel the read task so that a send task can be started.
            if (args.Task.Status.Equals("canceled") || args.Task.Status.Equals("aborted"))
                {
                Monitor.Enter(m_currentTask);
                Chilkat.Task t = m_currentTask;
                m_currentTask = new Chilkat.Task();
                Monitor.Exit(t);

                startNextTask();
                return;
                }

            if (args.Task.UserData.Equals("read"))
                {
                handleChannelReadCompleted(args);
                }
            else if (args.Task.UserData.Equals("send"))
                {
                handleChannelSendCompleted(args);
                }
            }

        // This is called when a key is pressed in the textbox.
        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
            {
            m_accumulatedKeyPresses.Append(e.KeyChar.ToString());

            Monitor.Enter(m_currentTask);

            if (m_currentTask.Live)
                {
                // If the current task is a read task, then cancel it.
                // The send task should automatically start because when the read task "finishes" because it was canceled,
                // the task completed handler will see that accumulated key presses are waiting to be sent,
                // and the send task will be started.
                if (m_currentTask.UserData.Equals("read"))
                    {
                    //appendToTxtDebug("(keypress handler) Cancelling the current read task...\r\n");
                    m_currentTask.Cancel();
                    }
                else
                    {
                    appendToTxtDebug("(keypress handler) Current task user data: " + m_currentTask.UserData + "\r\n");
                    }
                }

            Monitor.Exit(m_currentTask);

            e.Handled = true;

            textBox1.SelectionStart = textBox1.Text.Length;
            textBox1.ScrollToCaret();
            //Application.DoEvents();
            }


        }
    }
