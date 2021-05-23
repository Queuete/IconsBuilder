using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Abstract;
using ExileCore.Shared.Enums;
using IconsBuilder.Icons;
using JM.LinqFaster;
using SharpDX;

namespace IconsBuilder
{
    public class IconsBuilder : BaseSettingsPlugin<IconsBuilderSettings>
    {
        private string AlertFile => Path.Combine(DirectoryFullName, "config", "mod_alerts.txt");
        private string IgnoreFile => Path.Combine(DirectoryFullName, "config", "ignored_entities.txt");
        private List<string> IgnoredEntities { get; set; }
        private Dictionary<string, Size2> AlertEntitiesWithIconSize { get; set; } = new Dictionary<string, Size2>();
        private static EntityType[] Chests => new[]
        {
            EntityType.Chest,
            EntityType.SmallChest
        };
        private static EntityType[] SkippedEntityTypes => new []
        {
            EntityType.WorldItem, 
            EntityType.HideoutDecoration, 
            EntityType.Effect, 
            EntityType.Light, 
            EntityType.ServerObject, 
            EntityType.Daemon,
            EntityType.Error
        };

        private readonly Queue<Entity> _entities = new Queue<Entity>(128);

        private void ReadAlertFile()
        {
            var path = Path.Combine(DirectoryFullName, AlertFile);
            if (!File.Exists(AlertFile))
            {
                DebugWindow.LogError($"IconsBuilder -> Alert entities file does not exist. Path: {path}");
                return;
            }
            var readAllLines = File.ReadAllLines(AlertFile);

            foreach (var readAllLine in readAllLines)
            {
                if (readAllLine.StartsWith("#")) continue;
                var entityMetadata = readAllLine.Split(';');
                var iconSize = entityMetadata[2].Trim().Split(',');
                AlertEntitiesWithIconSize[entityMetadata[0]] = new Size2(int.Parse(iconSize[0]), int.Parse(iconSize[1]));
            }
        }
        private void ReadIgnoreFile()
        {
            var path = Path.Combine(DirectoryFullName, IgnoreFile);
            if (!File.Exists(path))
            {
                LogError($"IconsBuilder -> Ignored entities file does not exist. Path: {path}");
                return;
            }
            IgnoredEntities = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#")).ToList();
        }

        public override void OnLoad()
        {
            Graphics.InitImage("sprites.png");
        }

        public override void EntityAdded(Entity entity)
        {
            _entities.Enqueue(entity);
        }

        public override void AreaChange(AreaInstance area)
        {
            ReadIgnoreFile();
        }

        public override bool Initialise()
        {
            ReadAlertFile();           
            ReadIgnoreFile();
            return true;
        }

        public override Job Tick()
        {
            return new Job("IconsBuilder", AddIcons);
        }

        private void AddIcons()
        {
            while (_entities.Count > 0)
            {
                try
                {
                    var entity = _entities.Dequeue();
                    if (SkipIcon(entity)) continue;

                    var icon = GenerateIcon(entity);
                    if (icon == null) continue;
                    
                    entity.SetHudComponent(icon);
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError($"{nameof(IconsBuilder)} -> {ex}", 3);
                }
            }
        }

        private bool SkipIcon(Entity entity)
        {
            if (entity == null) return true;
            if (!entity.IsValid) return true;
            if (SkippedEntityTypes.AnyF(x => x == entity.Type)) return true;
            if (IgnoredEntities.AnyF(x => entity?.Path?.Contains(x) == true)) return true;

            return false;
        }

        private BaseIcon GenerateIcon(Entity entity)
        {
            //Monsters
            if (entity.Type == EntityType.Monster)
            {
                if (!entity.IsAlive) return null;

                if (entity.League == LeagueType.Legion)
                    return new LegionIcon(entity, GameController, Settings, AlertEntitiesWithIconSize);
                if (entity.League == LeagueType.Delirium)
                    return new DeliriumIcon(entity, GameController, Settings, AlertEntitiesWithIconSize);

                return new MonsterIcon(entity, GameController, Settings, AlertEntitiesWithIconSize);
            }

            //NPC
            if (entity.Type == EntityType.Npc)
                return new NpcIcon(entity, GameController, Settings);

            //Player
            if (entity.Type == EntityType.Player)
            {
                if (GameController.IngameState.Data.LocalPlayer.Address == entity.Address ||
                    GameController.IngameState.Data.LocalPlayer.GetComponent<Render>().Name == entity.RenderName) return null;

                if (!entity.IsValid) return null;
                return new PlayerIcon(entity, GameController, Settings, AlertEntitiesWithIconSize);
            }

            //Chests
            if (Chests.AnyF(x => x == entity.Type) && !entity.IsOpened)
                return new ChestIcon(entity, GameController, Settings);

            //Area transition
            if (entity.Type == EntityType.AreaTransition)
                return new MiscIcon(entity, GameController, Settings);

            //Shrine
            if (entity.HasComponent<Shrine>())
                return new ShrineIcon(entity, GameController, Settings);

            if (entity.HasComponent<Transitionable>() && entity.HasComponent<MinimapIcon>())
            {
                //Mission marker
                if (entity.Path.Equals("Metadata/MiscellaneousObjects/MissionMarker", StringComparison.Ordinal) ||
                    entity.GetComponent<MinimapIcon>().Name.Equals("MissionTarget", StringComparison.Ordinal))
                    return new MissionMarkerIcon(entity, GameController, Settings);

                return new MiscIcon(entity, GameController, Settings);
            }

            if (entity.HasComponent<MinimapIcon>() && entity.HasComponent<Targetable>())
                return new MiscIcon(entity, GameController, Settings);

            if (entity.Path.Contains("Metadata/Terrain/Leagues/Delve/Objects/EncounterControlObjects/AzuriteEncounterController"))
                return new MiscIcon(entity, GameController, Settings);

            if (entity.Type == EntityType.LegionMonolith) return new MiscIcon(entity, GameController, Settings);

            return null;
        }
    }
}
