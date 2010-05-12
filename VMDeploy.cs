using System;
using System.Text;
using System.Collections;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.IO;
using AppUtil;
using VimApi;

namespace VMDeploy
{
    class VMResource
    {
        public static String[] connectString = null;
        //--url https://localhost:4443/sdk/vimservice --server localhost --portnumber 4443 --ignorecert --username administrator --password Yv74aL5j  --operation rebootGuest --vmname DeployedTemplate
        public static void buildConnectString(string url, string server, string portnumber, string username, string password, string hostname)
        {
            string tempConnectString;
            tempConnectString = " --url " + url + " --server " + server + " --portnumber " + portnumber + " --username " + username + " --password " + password + " --ignorecert";
            connectString = tempConnectString.Trim().Split(new char[] {' '});
        }
    }

    class VMDeploy
    {
        static VimService _service;        

        private AppUtil.AppUtil cb = null;        
        private ServiceContent _sic;

        private string templateName;
        private string[] dnsList;
        private string workGroupPassword;
        private string hostName;
        private string domainAdmin;
        private string domainPassword;
        private string joinDomain;
        private string name; //Machine name
        private string productId;
        private string cloneName;
        private string vmPath;
        private string datacenterName;

        public VMDeploy(string hostName, string templateName, string name, string[] dnsList, string workGroupPassword, string domainAdmin, string domainPassword, string joinDomain, string productId, string cloneName, string datacenterName)
        {
            this.templateName = templateName;
            this.dnsList = dnsList;
            this.workGroupPassword = workGroupPassword;
            this.hostName = hostName;
            this.domainAdmin = domainAdmin;
            this.domainPassword = domainPassword;
            this.joinDomain = joinDomain;
            this.name = name;
            this.productId = productId;
            this.cloneName = cloneName;
            this.datacenterName = datacenterName;
            this.vmPath = "/" + this.datacenterName + "/vm/" + this.templateName;
        }

        private void deployVM()
        {
            _service = cb.getConnection()._service;
            _sic = cb.getConnection()._sic;
            
            // ManagedObjectReferences
            ManagedObjectReference datacenterRef;
            ManagedObjectReference vmFolderRef;
            ManagedObjectReference vmRef; 
            ManagedObjectReference hfmor; // hostFolder reference
            ArrayList crmors; // ArrayList of ComputeResource references
            ManagedObjectReference hostmor;
            ManagedObjectReference crmor = null; // ComputeResource reference
            ManagedObjectReference resourcePool;

            // Find the Datacenter reference by using findByInventoryPath().
            datacenterRef = _service.FindByInventoryPath(_sic.searchIndex, this.datacenterName);

            if (datacenterRef == null)
            {
                Console.WriteLine("The specified datacenter is not found");
                return;
            }

            // Find the virtual machine folder for this datacenter.
            vmFolderRef = (ManagedObjectReference)cb.getServiceUtil().GetMoRefProp(datacenterRef, "vmFolder");
            if (vmFolderRef == null)
            {
                Console.WriteLine("The virtual machine is not found");
                return;
            }

            vmRef = _service.FindByInventoryPath(_sic.searchIndex, this.vmPath);
            if (vmRef == null)
            {
                Console.WriteLine("The virtual machine is not found");
                return;
            }

            // Code for obtaining managed object reference to resource root

            hfmor = cb.getServiceUtil().GetMoRefProp(datacenterRef, "hostFolder");   
            crmors = cb.getServiceUtil().GetDecendentMoRefs(hfmor, "ComputeResource", null);         

            if (this.hostName != null)
            {
                hostmor = cb.getServiceUtil().GetDecendentMoRef(hfmor, "HostSystem", this.hostName);
                if (hostmor == null)
                {
                    Console.WriteLine("Host " + this.hostName + " not found");
                    return;
                }
            }
            else
            {
                hostmor = cb.getServiceUtil().GetFirstDecendentMoRef(datacenterRef, "HostSystem");
            }
            
            hostName = (String)cb.getServiceUtil().GetDynamicProperty(hostmor, "name");
            for (int i = 0; i < crmors.Count; i++)
            {

                ManagedObjectReference[] hrmors
                   = (ManagedObjectReference[])cb.getServiceUtil().GetDynamicProperty((ManagedObjectReference)crmors[i], "host");
                if (hrmors != null && hrmors.Length > 0)
                {
                    for (int j = 0; j < hrmors.Length; j++)
                    {
                        String hname = (String)cb.getServiceUtil().GetDynamicProperty(hrmors[j], "name");
                        if (hname.Equals(this.hostName))
                        {
                            crmor = (ManagedObjectReference)crmors[i];
                            i = crmors.Count + 1;
                            j = hrmors.Length + 1;
                        }

                    }
                }
            }

            if (crmor == null)
            {
                Console.WriteLine("No Compute Resource Found On Specified Host");
                return;
            }
            resourcePool = cb.getServiceUtil().GetMoRefProp(crmor, "resourcePool");

            /***********************************/
            /*Setup cloning sysprep preferences*/
            /***********************************/

            VirtualMachineCloneSpec cloneSpec = new VirtualMachineCloneSpec();
            VirtualMachineRelocateSpec relocSpec = new VirtualMachineRelocateSpec();

            // Set resource pool for relocspec(compulsory since deploying template)
            relocSpec.pool = resourcePool;

            cloneSpec.location = relocSpec;
            cloneSpec.powerOn = false; //Specifies whether or not the new VirtualMachine should be powered on after creation. As part of a customization, this flag is normally set to true, since the first power-on operation completes the customization process. This flag is ignored if a template is being created. 
            cloneSpec.template = false; //Specifies whether or not the new virtual machine should be marked as a template. 

            // Customization
            CustomizationSpec custSpec = new CustomizationSpec();

            // Make NIC settings
            CustomizationAdapterMapping[] custAdapter = new CustomizationAdapterMapping[1];
            custAdapter[0] = new CustomizationAdapterMapping();
            CustomizationIPSettings custIPSettings = new CustomizationIPSettings();
            CustomizationDhcpIpGenerator custDhcp = new CustomizationDhcpIpGenerator();
            custIPSettings.ip = custDhcp;
            custAdapter[0].adapter = custIPSettings;
            // Set NIC settings
            custSpec.nicSettingMap = custAdapter;

            // Make DNS entry
            CustomizationGlobalIPSettings custIP = new CustomizationGlobalIPSettings();            
            custIP.dnsServerList = dnsList;
            // Set DNS entry
            custSpec.globalIPSettings = custIP;
            
            // Make Sysprep entries
            CustomizationSysprep custPrep = new CustomizationSysprep(); //An object representation of a Windows sysprep.inf answer file. The sysprep type encloses all the individual keys listed in a sysprep.inf file

            // Make guiRunOnce entries(to change autologon settings to login to domain)

            CustomizationGuiRunOnce custGuiRunOnce = new CustomizationGuiRunOnce();

            string deleteKey = "reg delete \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\" /v \"DefaultDomainName\" /f";
            string addKey = "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\" /v \"DefaultDomainName\" /t REG_SZ /d " + this.joinDomain;
            string shutdownKey = "shutdown -r -t 00 -c \"Rebooting computer\"";

            custGuiRunOnce.commandList = new string[] { deleteKey, addKey, shutdownKey };
        
            // Set guiRunOnce
            custPrep.guiRunOnce = custGuiRunOnce;

            // Make guiUnattended settings
            CustomizationGuiUnattended custGui = new CustomizationGuiUnattended(); //The GuiUnattended type maps to the GuiUnattended key in the sysprep.inf answer file
            custGui.autoLogon = true; //The GuiUnattended type maps to the GuiUnattended key in the sysprep.inf answer file
            custGui.autoLogonCount = 4; //If the AutoLogon flag is set, then the AutoLogonCount property specifies the number of times the machine should automatically log on as Administrator
            
            CustomizationPassword custWorkPass = new CustomizationPassword();

            if (this.workGroupPassword != null)
            {
                custWorkPass.plainText = true; //Flag to specify whether or not the password is in plain text, rather than encrypted. 
                custWorkPass.value = this.workGroupPassword;
                custGui.password = custWorkPass;
            }

            custGui.timeZone = 190; //IST The time zone for the new virtual machine. Numbers correspond to time zones listed in sysprep documentation at  in Microsoft Technet. Taken from unattend.txt
            
            // Set guiUnattend settings
            custPrep.guiUnattended = custGui;

            // Make identification settings
            CustomizationIdentification custId = new CustomizationIdentification();
            custId.domainAdmin = this.domainAdmin;
            CustomizationPassword custPass = new CustomizationPassword();
            custPass.plainText = true; //Flag to specify whether or not the password is in plain text, rather than encrypted. 
            custPass.value = this.domainPassword;
            custId.domainAdminPassword = custPass;
            custId.joinDomain = this.joinDomain;
            // Set identification settings
            custPrep.identification = custId;

            // Make userData settings
            CustomizationUserData custUserData = new CustomizationUserData();
            CustomizationFixedName custName = new CustomizationFixedName();
            custName.name = this.name;
            custUserData.computerName = custName;
            custUserData.fullName = "ePO";
            custUserData.orgName = "McAfee";

            if (this.productId != null)
            {
                custUserData.productId = this.productId;
            }

            // Set userData settings
            custPrep.userData = custUserData;

            // Set sysprep
            custSpec.identity = custPrep;

            // clonespec customization
            cloneSpec.customization = custSpec;

            // clone power on
            cloneSpec.powerOn = true;

            String clonedName = cloneName;
            Console.WriteLine("Launching clone task to create a clone: " + clonedName);

            try
            {
                ManagedObjectReference cloneTask
                   = _service.CloneVM_Task(vmRef, vmFolderRef, clonedName, cloneSpec);
                String status = cb.getServiceUtil().WaitForTask(cloneTask);
                if (status.Equals("failure"))
                {
                    Console.WriteLine("Failure -: Virtual Machine cannot be cloned");
                }
                if (status.Equals("sucess"))
                {
                    Console.WriteLine("Virtual Machine Cloned  successfully.");
                }
                else
                {
                    Console.WriteLine("Virtual Machine Cloned cannot be cloned");
                }
            }
            catch (Exception e)
            {

            }
        }

        public bool deploy()
        {
            if (VMResource.connectString != null)
            {
                cb = AppUtil.AppUtil.initialize("VMDeploy", VMResource.connectString);
                this.deployVM();
                cb.disConnect();
                
                /* Insert code to monitor VM until agent is in place */

                return true;
            }
            else
            {
                Console.Write("No connect string set, unable to connect");
                return false;
            }
        }

        public static void Main(String[] args)
        {
            //VMDeploy obj = new VMDeploy();
            //cb = AppUtil.AppUtil.initialize("VMDeploy"
            //                        , VMDeploy.constructOptions()
            //                       , args);
            //cb.connect();
            //obj.deployVM();
            //cb.disConnect();
            //Console.WriteLine("Press any key to exit: ");
            //Console.Read();
            //Environment.Exit(1);
            VMResource.buildConnectString("https://localhost:4443/sdk/vimservice","localhost","4443","administrator","Yv74aL5j","Automation");
            VMDeploy newMachine = new VMDeploy("172.16.196.23", "Win2008x32Ent", "NewTest282", new string[] { "172.16.196.41" }, "Yv74aL5j", "administrator", "Yv74aL5j", "autoepo.com", "FFF9X-DY39K-2G87P-KQ4M7-QMWKT", "MyTestClone", "Automation");
            newMachine.deploy();

        }
    }
}