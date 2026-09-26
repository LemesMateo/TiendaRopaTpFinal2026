# Referencias de diseño y producto — Velvet Rose Boutique

## Fuente de verdad

- `docs/Propuesta_Proyecto_Velvet_Rose.docx`: alcance, objetivos, actores, casos de uso, 12 entidades y pantallas de referencia. Consultarlo antes de desarrollar.
- `docs/DER.png`: modelo de datos de la propuesta.
- `docs/Casos_de_Uso.png`: diagrama de casos de uso de la propuesta.
- `docs/mockups/*.png`: pantallas de referencia **extraídas del documento original**, con nombres corregidos según el contenido visible (los títulos del DOCX original presentan desplazamientos).

## Identidad visual observada en los mockups

Marca: **Velvet Rose Boutique**. Logotipo con una rosa roja/bordó y hojas ciruela/morado. Estilo de boutique femenina, elegante y cálida, con fondo marfil/blanco cálido, botones destacados rojo frambuesa/bordó, acentos rosados suaves y tonos ciruela/púrpura en detalles y precios. Tipografía con contraste entre títulos con serif elegante y texto funcional sans-serif; tarjetas blancas con esquinas redondeadas y sombras discretas. Galerías de fotos de prendas, filtros en chips y navegación inferior en la app móvil. **Tomar los colores exactos y espaciados de las capturas**, no tratar estos descriptores como un sistema de tokens definitivo.

El documento de gestión personal puede tener púrpura dominante, pero las capturas de la identidad **actual de la app** tienen acento principal frambuesa/bordó. NO reemplazarlo por púrpura intenso sin una indicación expresa del propietario. El púrpura/ciruela funciona actualmente como acento secundario.

## Instrucciones para la implementación

1. Revisar por separado cada PNG de `docs/mockups`. Mantener nombres, jerarquía, layout y flujos antes de introducir mejoras.
2. Los mockups son **referencia visual**, no funcionalidad implementada. No afirmar que existe backend, autenticación social ni pagos por el hecho de que figuren en una imagen.
3. Algunas pantallas contienen elementos fuera del MVP (favoritos, botones Google/Apple, promociones, códigos de descuento, métricas comerciales): pueden conservarse como maquetas visuales si conviene, pero no implementar lógica sin aprobación del propietario.
4. Estos mockups fueron diseñados para teléfono. Crear variaciones responsive coherentes para tablet, escritorio web y Windows; no agrandar la imagen de móvil ni inventar sin más pantallas completamente distintas.
5. Usar Material Symbols como iconografía, no importar Material UI de React. Crear componente Razor reutilizable.
6. Mantener componentes Razor y tokens comunes en `TiendaRopa.Shared`; dejar configuración y servicios exclusivos de MAUI/Web en sus respectivos proyectos.
7. Las fotos de prendas incluidas en prototipos son referencias de diseño; revisar derechos y obtener activos aptos para publicación antes de usarlas comercialmente.

## Relación de imágenes

| Archivo | Contenido |
|---|---|
| 01_login.png | Inicio de sesión |
| 02_registro.png | Crear cuenta |
| 03_inicio_cliente.png | Inicio, novedades y tarjetas de productos |
| 04_catalogo_filtros.png | Catálogo, búsqueda y filtros |
| 05_detalle_producto.png | Galería, variantes, cantidad y añadir al carrito |
| 06_carrito.png | Carrito y confirmación |
| 07_mis_pedidos.png | Estado e historial de pedidos |
| 08_panel_administrador.png | Panel inicial del administrador |
| 09_gestion_productos.png | Listado y gestión de productos/stock |
