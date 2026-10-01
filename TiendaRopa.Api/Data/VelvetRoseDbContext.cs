using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Entities;

namespace TiendaRopa.Api.Data;

/// <summary>
/// Contexto de Velvet Rose Boutique. 12 entidades según el DER de la propuesta.
/// </summary>
public class VelvetRoseDbContext(DbContextOptions<VelvetRoseDbContext> options) : DbContext(options)
{
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoImagen> ProductoImagenes => Set<ProductoImagen>();
    public DbSet<Talle> Talles => Set<Talle>();
    public DbSet<Color> Colores => Set<Color>();
    public DbSet<ProductoVariante> ProductoVariantes => Set<ProductoVariante>();
    public DbSet<EstadoPedido> EstadosPedido => Set<EstadoPedido>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoDetalle> PedidoDetalles => Set<PedidoDetalle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---------- Identidad ----------
        modelBuilder.Entity<Rol>(e =>
        {
            e.ToTable("Rol");
            e.HasKey(x => x.IdRol);
            e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("Usuario");
            e.HasKey(x => x.IdUsuario);
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Rol)
                .WithMany(r => r.Usuarios)
                .HasForeignKey(x => x.IdRol)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("Cliente");
            e.HasKey(x => x.IdCliente);
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.Property(x => x.Apellido).HasMaxLength(100).IsRequired();
            e.Property(x => x.Telefono).HasMaxLength(50);
            e.Property(x => x.Direccion).HasMaxLength(300);
            // Relación 1:1 con Usuario
            e.HasIndex(x => x.IdUsuario).IsUnique();
            e.HasOne(x => x.Usuario)
                .WithOne(u => u.Cliente)
                .HasForeignKey<Cliente>(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Catálogo ----------
        modelBuilder.Entity<Categoria>(e =>
        {
            e.ToTable("Categoria");
            e.HasKey(x => x.IdCategoria);
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<Producto>(e =>
        {
            e.ToTable("Producto");
            e.HasKey(x => x.IdProducto);
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.PrecioBase).HasPrecision(18, 2);
            e.HasIndex(x => x.IdCategoria);
            e.HasIndex(x => x.Activo);
            e.HasOne(x => x.Categoria)
                .WithMany(c => c.Productos)
                .HasForeignKey(x => x.IdCategoria)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductoImagen>(e =>
        {
            e.ToTable("ProductoImagen");
            e.HasKey(x => x.IdProductoImagen);
            e.Property(x => x.UrlImagen).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => x.IdProducto);
            // Como máximo una imagen principal por producto
            e.HasIndex(x => x.IdProducto)
                .IsUnique()
                .HasFilter("[Principal] = 1");
            e.HasOne(x => x.Producto)
                .WithMany(p => p.Imagenes)
                .HasForeignKey(x => x.IdProducto)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Talle>(e =>
        {
            e.ToTable("Talle");
            e.HasKey(x => x.IdTalle);
            e.Property(x => x.Nombre).HasMaxLength(20).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<Color>(e =>
        {
            e.ToTable("Color");
            e.HasKey(x => x.IdColor);
            e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
            e.Property(x => x.CodigoHex).HasMaxLength(7);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<ProductoVariante>(e =>
        {
            e.ToTable("ProductoVariante");
            e.HasKey(x => x.IdProductoVariante);
            e.Property(x => x.Precio).HasPrecision(18, 2);
            e.ToTable("ProductoVariante", t =>
                t.HasCheckConstraint("CK_ProductoVariante_Stock_NoNegativo", "[Stock] >= 0"));
            // Regla central: una sola variante por combinación producto+talle+color
            e.HasIndex(x => new { x.IdProducto, x.IdTalle, x.IdColor }).IsUnique();
            e.HasOne(x => x.Producto)
                .WithMany(p => p.Variantes)
                .HasForeignKey(x => x.IdProducto)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Talle)
                .WithMany(t => t.Variantes)
                .HasForeignKey(x => x.IdTalle)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Color)
                .WithMany(c => c.Variantes)
                .HasForeignKey(x => x.IdColor)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Pedidos ----------
        modelBuilder.Entity<EstadoPedido>(e =>
        {
            e.ToTable("EstadoPedido");
            e.HasKey(x => x.IdEstadoPedido);
            e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("Pedido");
            e.HasKey(x => x.IdPedido);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.Observacion).HasMaxLength(1000);
            e.HasIndex(x => x.IdCliente);
            e.HasOne(x => x.Cliente)
                .WithMany(c => c.Pedidos)
                .HasForeignKey(x => x.IdCliente)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EstadoPedido)
                .WithMany(est => est.Pedidos)
                .HasForeignKey(x => x.IdEstadoPedido)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PedidoDetalle>(e =>
        {
            e.ToTable("PedidoDetalle");
            e.HasKey(x => x.IdPedidoDetalle);
            e.Property(x => x.PrecioUnitario).HasPrecision(18, 2);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.ToTable("PedidoDetalle", t =>
                t.HasCheckConstraint("CK_PedidoDetalle_Cantidad_Positiva", "[Cantidad] > 0"));
            e.HasOne(x => x.Pedido)
                .WithMany(p => p.Detalles)
                .HasForeignKey(x => x.IdPedido)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ProductoVariante)
                .WithMany(v => v.Detalles)
                .HasForeignKey(x => x.IdProductoVariante)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
