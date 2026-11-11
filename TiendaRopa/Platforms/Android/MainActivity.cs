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

            // La ventana se redimensiona al abrir el teclado: el WebView
            // encoge y el formulario se puede desplazar hasta el campo
            // enfocado en lugar de quedar tapado por el teclado.
            Window?.SetSoftInputMode(SoftInput.AdjustResize);

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
        /// MAUI 9+ fuerza edge-to-edge en Android (SetDecorFitsSystemWindows
        /// = false) y el ContentPage viene con SafeAreaEdges = None, así que el
        /// WebView queda dibujado por debajo de las barras del sistema: el
        /// inset se aplica AQUÍ, a nivel de ventana, una sola vez.
        ///
        /// El CSS compartido usa env(safe-area-inset-*), que en el WebView de
        /// Android devuelve el recorte de la cámara y no la barra de estado.
        /// Para no sumar dos veces el mismo espacio, el host (wwwroot/index.html)
        /// marca el documento con .vr-safe-area-native en Android y el CSS
        /// compartido deja de aplicar los insets por su cuenta.
        ///
        /// La barra de estado y la de navegación siguen visibles: no se ocultan.
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

            // Con el teclado abierto la barra de navegación queda tapada:
            // en ese caso su espacio no hay que reservarlo.
            var tecladoVisible = insets.IsVisible(WindowInsets.Type.Ime());
            var abajo = tecladoVisible ? 0 : area.Bottom;

            if (decor.PaddingLeft == area.Left
                && decor.PaddingTop == area.Top
                && decor.PaddingRight == area.Right
                && decor.PaddingBottom == abajo)
            {
                return;
            }

            decor.SetBackgroundColor(FondoMarca);
            decor.SetPadding(area.Left, area.Top, area.Right, abajo);
        }
    }
}
