using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using patternbook;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace PatternBook.Tests
{
    /// <summary>
    /// ConfigKit, if the player has it: Pattern Book's settings on its settings screen.
    ///
    /// The binding is by reflection, against a method name in a mod this one does not reference.
    /// If ConfigKit renames or resignatures it, nothing would notice: the mod would carry on
    /// working and simply drop off the settings screen. That is what these are for.
    ///
    /// The ConfigKit ones no-op when it is not installed. Run them with it on the mod path:
    /// tests/fixtures/fetch.sh fetches it alongside the other fixtures.
    /// </summary>
    [RequiresClient]
    public class CompatConfigKit
    {
        static bool Installed => Capi.ModLoader.IsModEnabled("configkit");

        static bool Absent(string what)
        {
            if (Installed) return false;
            Log($"  ConfigKit not installed - {what} not checked");
            return true;
        }

        static object ConfigKit => Capi.ModLoader.GetModSystem("ConfigKit.ConfigKitModSystem");

        [VsTest]
        public async Task TheConfigIsHandedToConfigKit()
        {
            await OnClient();
            if (Absent("the binding")) return;

            Assert.True(PatternBookModSystem.ConfigKitBound,
                "ConfigKit holds the config - if false, the method was not found, threw, or refused it (see the log)");
        }

        [VsTest]
        public async Task ConfigKitStillHasTheMethodsWeCallByName()
        {
            await OnClient();
            if (Absent("the methods")) return;

            var system = ConfigKit;
            Assert.NotNull(system, "ConfigKit.ConfigKitModSystem still exists under that name");

            MethodInfo register = system.GetType().GetMethod("RegisterManagedConfig");
            Assert.NotNull(register, "RegisterManagedConfig still exists");
            var types = register.GetParameters().Select(p => p.ParameterType.Name).ToArray();
            Log("  RegisterManagedConfig(" + string.Join(", ", types) + ")");
            Assert.Equal(6, types.Length, "and still takes the six arguments this mod passes");

            Assert.NotNull(system.GetType().GetMethod("GetConfig"), "GetConfig still exists");
        }

        [VsTest]
        public async Task ConfigKitSeesEverySettingAsClientSide()
        {
            await OnClient();
            if (Absent("the settings")) return;

            // A server-owned setting would be read-only on a ConfigKit server, and overwritten by
            // values from a server that does not have this mod
            var system = ConfigKit;
            var getSetting = system.GetType().GetMethod("GetSetting");
            foreach (var prop in typeof(PatternBookConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object setting = getSetting.Invoke(system, ["patternbook", prop.Name]);
                Assert.NotNull(setting, $"ConfigKit has {prop.Name}");
                bool clientSide = (bool)setting.GetType().GetProperty("ClientSide").GetValue(setting);
                Assert.True(clientSide, $"{prop.Name} is client-side in ConfigKit");
            }
        }

        [VsTest]
        public async Task EverySettingIsDescribedAndClientSide()
        {
            // ConfigKit reflects over the config object, so these attributes are its entire
            // schema. Checked with or without ConfigKit, so a new setting cannot slip in bare.
            var props = typeof(PatternBookConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var undescribed = props.Where(p => p.GetCustomAttribute<DescriptionAttribute>() == null).Select(p => p.Name).ToArray();
            var serverOwned = props.Where(p => p.GetCustomAttribute<CategoryAttribute>()?.Category.Contains("clientside") != true).Select(p => p.Name).ToArray();

            Log($"  {props.Length} settings");
            Assert.Equal(0, undescribed.Length, "every setting is described: " + string.Join(", ", undescribed));
            Assert.Equal(0, serverOwned.Length, "every setting is clientside: " + string.Join(", ", serverOwned));
            await Task.CompletedTask;
        }
    }
}
