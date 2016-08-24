using System.ComponentModel;
using System.Configuration;
using System.IO;
using Zeta.Common.Xml;
using Zeta.Game;
using Zeta.XmlEngine;

namespace ItemLog
{
    [XmlElement("ItemLogSettings")]
    class Settings : XmlSettings
    {
        private static Settings _instance;
        private int rarity;

        private static string _battleTagName;

        public static string BattleTagName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_battleTagName) && ZetaDia.Service.Hero.IsValid)
                    _battleTagName = ZetaDia.Service.Hero.BattleTagName;
                return _battleTagName;
            }
        }

        public Settings() :
            base(Path.Combine(SettingsDirectory, BattleTagName, "ItemLogSettings.xml"))
        {

        }

        public static Settings Instance
        {
            get { return _instance ?? (_instance = new Settings()); }
        }

        [XmlElement("Rarity")]
        [DefaultValue(3)]
        public int Rarity
        {
            get
            {
                return rarity;
            }
            set
            {
                rarity = value;
                OnPropertyChanged("Rarity");
            }
        }

    }
}
