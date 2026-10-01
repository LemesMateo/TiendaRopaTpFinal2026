using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiendaRopa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categoria",
                columns: table => new
                {
                    IdCategoria = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categoria", x => x.IdCategoria);
                });

            migrationBuilder.CreateTable(
                name: "Color",
                columns: table => new
                {
                    IdColor = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CodigoHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Color", x => x.IdColor);
                });

            migrationBuilder.CreateTable(
                name: "EstadoPedido",
                columns: table => new
                {
                    IdEstadoPedido = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadoPedido", x => x.IdEstadoPedido);
                });

            migrationBuilder.CreateTable(
                name: "Rol",
                columns: table => new
                {
                    IdRol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rol", x => x.IdRol);
                });

            migrationBuilder.CreateTable(
                name: "Talle",
                columns: table => new
                {
                    IdTalle = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Talle", x => x.IdTalle);
                });

            migrationBuilder.CreateTable(
                name: "Producto",
                columns: table => new
                {
                    IdProducto = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdCategoria = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PrecioBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Producto", x => x.IdProducto);
                    table.ForeignKey(
                        name: "FK_Producto_Categoria_IdCategoria",
                        column: x => x.IdCategoria,
                        principalTable: "Categoria",
                        principalColumn: "IdCategoria",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuario",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IdRol = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaAlta = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuario", x => x.IdUsuario);
                    table.ForeignKey(
                        name: "FK_Usuario_Rol_IdRol",
                        column: x => x.IdRol,
                        principalTable: "Rol",
                        principalColumn: "IdRol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductoImagen",
                columns: table => new
                {
                    IdProductoImagen = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdProducto = table.Column<int>(type: "int", nullable: false),
                    UrlImagen = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Principal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoImagen", x => x.IdProductoImagen);
                    table.ForeignKey(
                        name: "FK_ProductoImagen_Producto_IdProducto",
                        column: x => x.IdProducto,
                        principalTable: "Producto",
                        principalColumn: "IdProducto",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductoVariante",
                columns: table => new
                {
                    IdProductoVariante = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdProducto = table.Column<int>(type: "int", nullable: false),
                    IdTalle = table.Column<int>(type: "int", nullable: false),
                    IdColor = table.Column<int>(type: "int", nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoVariante", x => x.IdProductoVariante);
                    table.CheckConstraint("CK_ProductoVariante_Stock_NoNegativo", "[Stock] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductoVariante_Color_IdColor",
                        column: x => x.IdColor,
                        principalTable: "Color",
                        principalColumn: "IdColor",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductoVariante_Producto_IdProducto",
                        column: x => x.IdProducto,
                        principalTable: "Producto",
                        principalColumn: "IdProducto",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductoVariante_Talle_IdTalle",
                        column: x => x.IdTalle,
                        principalTable: "Talle",
                        principalColumn: "IdTalle",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cliente",
                columns: table => new
                {
                    IdCliente = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Apellido = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cliente", x => x.IdCliente);
                    table.ForeignKey(
                        name: "FK_Cliente_Usuario_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuario",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pedido",
                columns: table => new
                {
                    IdPedido = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdCliente = table.Column<int>(type: "int", nullable: false),
                    IdEstadoPedido = table.Column<int>(type: "int", nullable: false),
                    FechaPedido = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observacion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedido", x => x.IdPedido);
                    table.ForeignKey(
                        name: "FK_Pedido_Cliente_IdCliente",
                        column: x => x.IdCliente,
                        principalTable: "Cliente",
                        principalColumn: "IdCliente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pedido_EstadoPedido_IdEstadoPedido",
                        column: x => x.IdEstadoPedido,
                        principalTable: "EstadoPedido",
                        principalColumn: "IdEstadoPedido",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PedidoDetalle",
                columns: table => new
                {
                    IdPedidoDetalle = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdPedido = table.Column<int>(type: "int", nullable: false),
                    IdProductoVariante = table.Column<int>(type: "int", nullable: false),
                    Cantidad = table.Column<int>(type: "int", nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoDetalle", x => x.IdPedidoDetalle);
                    table.CheckConstraint("CK_PedidoDetalle_Cantidad_Positiva", "[Cantidad] > 0");
                    table.ForeignKey(
                        name: "FK_PedidoDetalle_Pedido_IdPedido",
                        column: x => x.IdPedido,
                        principalTable: "Pedido",
                        principalColumn: "IdPedido",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PedidoDetalle_ProductoVariante_IdProductoVariante",
                        column: x => x.IdProductoVariante,
                        principalTable: "ProductoVariante",
                        principalColumn: "IdProductoVariante",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categoria_Nombre",
                table: "Categoria",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cliente_IdUsuario",
                table: "Cliente",
                column: "IdUsuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Color_Nombre",
                table: "Color",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstadoPedido_Nombre",
                table: "EstadoPedido",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedido_IdCliente",
                table: "Pedido",
                column: "IdCliente");

            migrationBuilder.CreateIndex(
                name: "IX_Pedido_IdEstadoPedido",
                table: "Pedido",
                column: "IdEstadoPedido");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoDetalle_IdPedido",
                table: "PedidoDetalle",
                column: "IdPedido");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoDetalle_IdProductoVariante",
                table: "PedidoDetalle",
                column: "IdProductoVariante");

            migrationBuilder.CreateIndex(
                name: "IX_Producto_Activo",
                table: "Producto",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "IX_Producto_IdCategoria",
                table: "Producto",
                column: "IdCategoria");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoImagen_IdProducto",
                table: "ProductoImagen",
                column: "IdProducto",
                unique: true,
                filter: "[Principal] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoVariante_IdColor",
                table: "ProductoVariante",
                column: "IdColor");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoVariante_IdProducto_IdTalle_IdColor",
                table: "ProductoVariante",
                columns: new[] { "IdProducto", "IdTalle", "IdColor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductoVariante_IdTalle",
                table: "ProductoVariante",
                column: "IdTalle");

            migrationBuilder.CreateIndex(
                name: "IX_Rol_Nombre",
                table: "Rol",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Talle_Nombre",
                table: "Talle",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuario_Email",
                table: "Usuario",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuario_IdRol",
                table: "Usuario",
                column: "IdRol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PedidoDetalle");

            migrationBuilder.DropTable(
                name: "ProductoImagen");

            migrationBuilder.DropTable(
                name: "Pedido");

            migrationBuilder.DropTable(
                name: "ProductoVariante");

            migrationBuilder.DropTable(
                name: "Cliente");

            migrationBuilder.DropTable(
                name: "EstadoPedido");

            migrationBuilder.DropTable(
                name: "Color");

            migrationBuilder.DropTable(
                name: "Producto");

            migrationBuilder.DropTable(
                name: "Talle");

            migrationBuilder.DropTable(
                name: "Usuario");

            migrationBuilder.DropTable(
                name: "Categoria");

            migrationBuilder.DropTable(
                name: "Rol");
        }
    }
}
