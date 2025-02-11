using Microsoft.Win32;
using System.Text;
using System.Text.Json.Nodes;

namespace AutoKeyLight
{
    public partial class MainForm : Form
    {
        private readonly HttpClient httpClient = new HttpClient();
        const string webcamRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
        RegistryKey? startWithWindowsRegistry = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        bool isCameraOn = false;
        string keylightURL = $"http://{Properties.Settings.Default.IP}:9123/elgato/lights";
        CancellationTokenSource requestStateTokenSource;
        CancellationToken requestStateToken;
        System.Drawing.Icon TrayIconUnlit;
        System.Drawing.Icon TrayIconLit;

        public MainForm()
        {
            InitializeComponent();
            requestStateTokenSource = new CancellationTokenSource();
            requestStateToken = requestStateTokenSource.Token;

            TrayIconLit = Icon.FromHandle(Properties.Resources.TrayIconLit.GetHicon());
            TrayIconUnlit = Icon.FromHandle(Properties.Resources.TrayIconUnlit.GetHicon());
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            RefreshCameraState();
            tmrCameraCheck_Tick(sender, e);
            txtIP.Text = Properties.Settings.Default.IP;
            niTray.Icon = TrayIconUnlit;
            this.Icon = Properties.Resources.Icon;
        }

        private async void RefreshCameraState()
        {
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(keylightURL, requestStateToken);

                if (response.IsSuccessStatusCode)
                {
                    bool success = false;

                    JsonNode? responseRoot = JsonNode.Parse(await response.Content.ReadAsStringAsync(requestStateToken));
                    if (responseRoot != null)
                    {
                        JsonNode? lightsArray = responseRoot.AsObject()["lights"];
                        if (lightsArray != null)
                        {
                            JsonNode? lightObject = lightsArray.AsArray()[0];
                            if (lightObject != null)
                            {
                                JsonNode? onProperty = lightObject.AsObject()["on"];
                                if (onProperty != null)
                                {
                                    isCameraOn = onProperty.GetValue<int>() == 1;
                                    lblLightState.Text = isCameraOn ? "Light is ON" : "Light is OFF";
                                    lblLightState.ForeColor = isCameraOn ? Color.LawnGreen : Color.IndianRed;
                                    success = true;
                                }
                            }
                        }
                    }

                    if (!success)
                    {
                        lblLightState.Text = "Malformed JSON please\ncreate an Issue on GitHub";
                        lblLightState.ForeColor = Color.Red;
                    }
                }
                else
                {
                    lblLightState.Text = "Error, check IP";
                    lblLightState.ForeColor = Color.Red;
                }
            }
            catch
            {
                lblLightState.Text = "Error, check IP";
                lblLightState.ForeColor = Color.Red;

                requestStateTokenSource = new CancellationTokenSource();
                requestStateToken = requestStateTokenSource.Token;
            }
        }

        private void CameraIsOn()
        {
            RefreshCameraState();

            lblCameraState.Text = "Camera is ON";
            lblCameraState.ForeColor = Color.LawnGreen;
            niTray.Icon = TrayIconLit;

            if (!isCameraOn)
            {
                StringContent httpContent = new StringContent("{\"lights\": [{\"on\": 1}]}", Encoding.UTF8, "application/json");
                try
                {
                    httpClient.PutAsync(keylightURL, httpContent).Wait();
                } catch { }
            }
        }

        private void CameraIsOff()
        {
            RefreshCameraState();
            niTray.Icon = TrayIconUnlit;

            lblCameraState.Text = "Camera is OFF";
            lblCameraState.ForeColor = Color.IndianRed;

            if (isCameraOn)
            {
                StringContent httpContent = new StringContent("{\"lights\": [{\"on\": 0}]}", Encoding.UTF8, "application/json");
                try
                {
                    httpClient.PutAsync(keylightURL, httpContent).Wait();
                }
                catch { }
            }
        }

        private void tmrCameraCheck_Tick(object sender, EventArgs e)
        {
            RegistryKey? webcamAppsKey = Registry.CurrentUser.OpenSubKey(webcamRegistryKey);

            if (webcamAppsKey == null)
                return;

            string[] packagedKeys = webcamAppsKey.GetSubKeyNames();

            foreach (string key in packagedKeys)
            {
                RegistryKey? subKey = webcamAppsKey.OpenSubKey(key);

                if (subKey == null)
                    continue;

                Int64? timestamp = (Int64?)subKey.GetValue("LastUsedTimeStop");

                if (timestamp != null && timestamp == 0)
                {
                    CameraIsOn();
                    return;
                }
            }

            RegistryKey? nonPackagedKey;
            if ((nonPackagedKey = webcamAppsKey.OpenSubKey("NonPackaged")) != null)
            {
                string[] nonPackagedKeys = nonPackagedKey.GetSubKeyNames();

                foreach (string key in nonPackagedKeys)
                {
                    RegistryKey? subKey = nonPackagedKey.OpenSubKey(key);

                    if (subKey == null)
                        continue;

                    Int64? timestamp = (Int64?)subKey.GetValue("LastUsedTimeStop");

                    if (timestamp != null && timestamp == 0)
                    {
                        CameraIsOn();
                        return;
                    }
                }
            }

            CameraIsOff();
        }

        private void txtIP_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.IP = txtIP.Text;
            Properties.Settings.Default.Save();

            keylightURL = $"http://{txtIP.Text}:9123/elgato/lights";

            requestStateTokenSource.Cancel();
        }

        private void chkStartWithWindows_CheckedChanged(object sender, EventArgs e)
        {
            if (startWithWindowsRegistry == null)
                return;

            if (chkStartWithWindows.Checked)
                startWithWindowsRegistry.SetValue("MyApp", Application.ExecutablePath);
            else
                startWithWindowsRegistry.DeleteValue("MyApp", false);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                niTray.Visible = true;
                this.Hide();
                e.Cancel = true;
            }
        }

        private void niTray_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            chkStartWithWindows.Checked = startWithWindowsRegistry != null && startWithWindowsRegistry.GetValue("MyApp") != null;

            if (chkStartWithWindows.Checked)
                this.Hide();
        }

        private void cmsTray_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            switch (e.ClickedItem.Name)
            {
                case "tsmiExit":
                    Application.Exit();
                    break;
                case "tsmiOpen":
                    this.Show();
                    break;
            }
        }
    }
}