using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace TiendaRopa
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // WindowInsets.GetInsets(Type.*) exige Android 11 (API 30).
        // En versiones anteriores el sistema ya no impone edge-to-edge.
        private const int MinSdkParaInsets = 30;

        // Mismo fondo que --vr-bg (#F8F4ED) para que el relleno de la safe
        // area continúe sin costuras el encabezado y el pie.
        private static readonly Android.Graphics.Color FondoMarca =
            Android.Graphics.Color.ParseColor("#F8F4ED");

        private Android.Views.View? _decor;
        private bool _suscripto;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _decor = Window?.DecorView;
            if (_decor is null)
            {
                return;
            }

            // Rotación, cambios de teclado virtual y multitarea vuelven a
            // reportar insets: se reaplica el padding en cada reflujo.
            if (!_suscripto)
            {
                _decor.LayoutChange += (_, _) => AplicarSafeArea();
                _suscripto = true;
            }

            AplicarSafeArea();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            // Los insets sólo están completos una vez la ventana tiene foco.
            if (hasFocus)
            {
                AplicarSafeArea();
            }
        }

        /// <summary>
        /// Reserva el espacio de la barra de estado y de la barra de gestos.
        ///
        /// Con targetSdk 35+ Android 15/16 fuerza edge-to-edge y el WebView
        /// de Blazor queda dibujado por debajo de ambas barras. La hoja de
        /// estilos compartida ya usa env(safe-area-inset-top/bottom), pero
        /// ese valor sólo lo resuelve el WebView de iOS; en Android devuelve
        /// 0. Por eso el padding se aplica aquí, a nivel de ventana: baja el
        /// encabezado bajo la barra de estado y sube la bottom nav sobre la
        /// barra de gestos, sin tocar TiendaRopa.Shared.
        /// </summary>
        private void AplicarSafeArea()
        {
            if (Build.VERSION.SdkInt < (BuildVersionCodes)MinSdkParaInsets)
            {
                return;
            }

            var decor = _decor ?? Window?.DecorView;
            if (decor is null)
            {
                return;
            }

            // RootWindowInsets es null hasta que la ventana se dibuja.
            var insets = decor.RootWindowInsets;
            if (insets is null)
            {
                return;
            }

            // DisplayCutout cubre el hueco de la cámara en pantallas con notch.
            var tipo = WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout();
            var area = insets.GetInsets(tipo);

            if (decor.PaddingLeft == area.Left
                && decor.PaddingTop == area.Top
                && decor.PaddingRight == area.Right
                && decor.PaddingBottom == area.Bottom)
            {
                return;
            }

            decor.SetBackgroundColor(FondoMarca);
            decor.SetPadding(area.Left, area.Top, area.Right, area.Bottom);
        }
    }
}
