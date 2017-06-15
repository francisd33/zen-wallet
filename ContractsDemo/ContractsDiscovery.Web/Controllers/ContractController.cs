﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.UI.WebControls;
using ContractsDiscovery.Web.App_Data;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Zen.RPC;
using Zen.RPC.Common;

namespace ContractsDiscovery.Web.Controllers
{
    public class ContractController : Controller
    {
        string _address = WebConfigurationManager.AppSettings["node"];

        public async Task<ActionResult> Index(string id)
        {
			var contractData = new App_Data.ContractData() { ActiveContract = new ActiveContract() };

			Directory.CreateDirectory(Path.Combine("db", "contracts"));
			Directory.CreateDirectory(Path.Combine("db", "asset-metadata"));

            var contractHash = HttpServerUtility.UrlTokenDecode(id);
			var file = $"{id}";

            string contractCode = null;
            var codeFile = Path.ChangeExtension(Path.Combine("db", "contracts", file), ".txt");
			if (System.IO.File.Exists(codeFile))
			{
				contractCode = System.IO.File.ReadAllText(codeFile);
				contractData.ActiveContract.Code = contractCode;
			}

            var assetMetaDataFile = Path.ChangeExtension(Path.Combine("db", "asset-metadata", file), ".json");
            if (System.IO.File.Exists(assetMetaDataFile))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(assetMetaDataFile);
                    var assetMetaData = JsonConvert.DeserializeObject<AssetMetadata>(json);

					contractData.ActiveContract.AssetName = assetMetaData.name;
					contractData.ActiveContract.AssetImageUrl = assetMetaData.imageUrl;
					contractData.ActiveContract.AssetMetadataVersion = assetMetaData.version;

                } catch
                {
					contractData.ActiveContract.AssetName = "Bad metadata";
                }
            }

			var result = await Client.Send<GetContractPointedOutputsResultPayload>(_address, new GetContractPointedOutputsPayload() { ContractHash = contractHash });

            contractData.ActiveContract.TotalAssets = result.Success ? (ulong)result.PointedOutputs.Count : 0UL;
			//var contractTotalAssetsResult = await Client.Send<GetContractTotalAssetsResultPayload>(address, new GetContractTotalAssetsPayload()
			//{
			//	Hash = new Wallet.core.Data.Address(Convert.ToBase64String(contractHash)).Bytes
			//});

			contractData.ActiveContract.Address = Convert.ToBase64String(contractHash);

			if (contractCode != null)
            {
				contractData.ActiveContract.Code = contractCode;
                Utils.SetContractInfo(contractData.ActiveContract, contractCode);
			}

			return View(contractData);
        }
    }
}
