﻿using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.ServiceAuth;

namespace OpenSim.Services.Connectors
{
    public class BaseServiceConnector
    {
        protected IServiceAuth m_Auth;

        public BaseServiceConnector() { }

        public BaseServiceConnector(IConfigSource config, string section)
        {
            Initialise(config, section);
        }

        public void Initialise(IConfigSource config, string section)
        {
            string authType = Util.GetConfigVarFromSections<string>(config, "AuthType", new string[] { "Network", section }, "None");

            switch (authType)
            {
                case "BasicHttpAuthentication":
                    m_Auth = new BasicHttpAuthentication(config, section);
                    break;
            }

        }
    }
}
