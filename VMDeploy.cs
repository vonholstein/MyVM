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
    class VMDeploy
    {
        private static AppUtil.AppUtil cb = null;
        static VimService _service;
        static ServiceContent _sic;
        private void cloneVM()
        {
            _service = cb.getConnection()._service;
            _sic = cb.getConnection()._sic;
            String cloneName = cb.get_option("CloneName");
            String vmPath = cb.get_option("vmPath");
            String datacenterName = cb.get_option("DatacenterName");


            // Find the Datacenter reference by using findByInventoryPath().
            ManagedObjectReference datacenterRef
               = _service.FindByInventoryPath(_sic.searchIndex, datacenterName);
            if (datacenterRef == null)
            {
                Console.WriteLine("The specified datacenter is not found");
                return;
            }



            // Find the virtual machine folder for this datacenter.
            ManagedObjectReference vmFolderRef
               = (ManagedObjectReference)cb.getServiceUtil().GetMoRefProp(datacenterRef, "vmFolder");
            if (vmFolderRef == null)
            {
                Console.WriteLine("The virtual machine is not found");
                return;
            }


            ManagedObjectReference vmRef
               = _service.FindByInventoryPath(_sic.searchIndex, vmPath);
            if (vmRef == null)
            {
                Console.WriteLine("The virtual machine is not found");
                return;
            }

            ManagedObjectReference hfmor
         = cb.getServiceUtil().GetMoRefProp(datacenterRef, "hostFolder");   
            ArrayList crmors
               = cb.getServiceUtil().GetDecendentMoRefs(hfmor, "ComputeResource", null);

            String hostName = "172.16.196.23";
            ManagedObjectReference hostmor;
            if (hostName != null)
            {
                hostmor = cb.getServiceUtil().GetDecendentMoRef(hfmor, "HostSystem", hostName);
                if (hostmor == null)
                {
                    Console.WriteLine("Host " + hostName + " not found");
                    return;
                }
            }
            else
            {
                hostmor = cb.getServiceUtil().GetFirstDecendentMoRef(datacenterRef, "HostSystem");
            }

            ManagedObjectReference crmor = null;
            hostName = (String)cb.getServiceUtil().GetDynamicProperty(hostmor, "name");
            for (int i = 0; i < crmors.Count; i++)
            {

                ManagedObjectReference[] hrmors
                   = (ManagedObjectReference[])cb.getServiceUtil().GetDynamicProperty((ManagedObjectReference)crmors[i], "host");
                if (hrmors != null && hrmors.Length > 0)
                {
                    for (int j = 0; j < hrmors.Length; j++)
                    {
                        String hname
                           = (String)cb.getServiceUtil().GetDynamicProperty(hrmors[j], "name");
                        if (hname.Equals(hostName))
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

            ManagedObjectReference resourcePool
               = cb.getServiceUtil().GetMoRefProp(crmor, "resourcePool");



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
            string[] dnsList = new string[1] { "172.16.196.41" };
            custIP.dnsServerList = dnsList;
            // Set DNS entry
            custSpec.globalIPSettings = custIP;
            
            // Make Sysprep entries
            CustomizationSysprep custPrep = new CustomizationSysprep(); //An object representation of a Windows sysprep.inf answer file. The sysprep type encloses all the individual keys listed in a sysprep.inf file

            // Make guiUnattended settings
            CustomizationGuiUnattended custGui = new CustomizationGuiUnattended(); //The GuiUnattended type maps to the GuiUnattended key in the sysprep.inf answer file
            custGui.autoLogon = true; //The GuiUnattended type maps to the GuiUnattended key in the sysprep.inf answer file
            custGui.autoLogonCount = 4; //If the AutoLogon flag is set, then the AutoLogonCount property specifies the number of times the machine should automatically log on as Administrator
            CustomizationPassword custWorkPass = new CustomizationPassword();
            custWorkPass.plainText = true; //Flag to specify whether or not the password is in plain text, rather than encrypted. 
            custWorkPass.value = "Yv74aL5j";
            custGui.password = custWorkPass;

            custGui.timeZone = 190; //The time zone for the new virtual machine. Numbers correspond to time zones listed in sysprep documentation at  in Microsoft Technet. Taken from unattend.txt
            // Set guiUnattend settings
            custPrep.guiUnattended = custGui;

            // Make identification settings
            CustomizationIdentification custId = new CustomizationIdentification();
            custId.domainAdmin = "administrator";
            CustomizationPassword custPass = new CustomizationPassword();
            custPass.plainText = true; //Flag to specify whether or not the password is in plain text, rather than encrypted. 
            custPass.value = "Yv74aL5j";
            custId.domainAdminPassword = custPass;
            custId.joinDomain = "autoepo.com";
            // Set identification settings
            custPrep.identification = custId;

            // Make userData settings
            CustomizationUserData custUserData = new CustomizationUserData();
            CustomizationFixedName custName = new CustomizationFixedName();
            custName.name = "TestDeploy2k8";
            custUserData.computerName = custName;
            custUserData.fullName = "ePO";
            custUserData.orgName = "McAfee";
            custUserData.productId = "FFF9X-DY39K-2G87P-KQ4M7-QMWKT";
            // Set userData settings
            custPrep.userData = custUserData;

            // Set sysprep
            custSpec.identity = custPrep;

            // clonespec customization
            cloneSpec.customization = custSpec;

            // clone power on
            cloneSpec.powerOn = true;

            String clonedName = cloneName;
            Console.WriteLine("Launching clone task to create a clone: "
                               + clonedName);
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
        public static OptionSpec[] constructOptions()
        {
            OptionSpec[] useroptions = new OptionSpec[3];
            useroptions[0] = new OptionSpec("DatacenterName", "String", 1
                                     , "Name of the Datacenter"
                                     , null);
            useroptions[1] = new OptionSpec("vmPath", "String", 1,
                                            "A path to the VM inventory, example:Datacentername/vm/vmname",
                                            null);
            useroptions[2] = new OptionSpec("CloneName", "String", 1,
                                            "Name of the Clone",
                                            null);
            return useroptions;
        }
        public static void Main(String[] args)
        {
            VMDeploy obj = new VMDeploy();
            cb = AppUtil.AppUtil.initialize("VMDeploy"
                                    , VMDeploy.constructOptions()
                                   , args);
            cb.connect();
            obj.cloneVM();
            cb.disConnect();
            Console.WriteLine("Press any key to exit: ");
            Console.Read();
            Environment.Exit(1);
        }
    }
}