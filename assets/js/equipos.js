(function () {
  'use strict';

  const URL_LETHORSOFT = 'https://lethorsoft.com';
  const URL_COMPRA = `${URL_LETHORSOFT}/comprar?producto=lethorpos`;
  const URL_DESCARGA = 'https://github.com/pc2x/Lethorpos-releases/releases/latest';

  document.documentElement.classList.add('js');

  document.addEventListener('DOMContentLoaded', () => {
    aplicar_enlaces();
    iniciar_header();
    iniciar_menu_movil();
    cargar_productos();

    const anio = document.getElementById('anio-actual');
    if (anio) {
      anio.textContent = String(new Date().getFullYear());
    }
  });

  function aplicar_enlaces() {
    document.querySelectorAll('[data-compra]').forEach((enlace) => {
      enlace.href = URL_COMPRA;
    });
    document.querySelectorAll('[data-descarga]').forEach((enlace) => {
      enlace.href = URL_DESCARGA;
    });
    document.querySelectorAll('[data-lethorsoft]').forEach((enlace) => {
      enlace.href = URL_LETHORSOFT;
    });
  }

  function iniciar_header() {
    const header = document.getElementById('site-header');
    if (!header) {
      return;
    }
    const actualizar = () => {
      header.classList.toggle('is-scrolled', window.scrollY > 24);
      header.classList.add('logo-visible');
    };
    actualizar();
    window.addEventListener('scroll', actualizar, { passive: true });
  }

  function iniciar_menu_movil() {
    const boton = document.getElementById('nav-toggle');
    const nav = document.getElementById('header-nav');
    const header = document.getElementById('site-header');
    if (!boton || !nav || !header) {
      return;
    }

    const icono = boton.querySelector('i');
    const cambiar = (abierto) => {
      nav.classList.toggle('is-open', abierto);
      header.classList.toggle('menu-open', abierto);
      boton.setAttribute('aria-expanded', String(abierto));
      boton.setAttribute('aria-label', abierto ? 'Cerrar menú' : 'Abrir menú');
      if (icono) {
        icono.classList.toggle('fa-bars', !abierto);
        icono.classList.toggle('fa-xmark', abierto);
      }
    };

    boton.addEventListener('click', () => cambiar(!nav.classList.contains('is-open')));
    nav.querySelectorAll('a').forEach((enlace) => enlace.addEventListener('click', () => cambiar(false)));
    document.addEventListener('keydown', (evento) => {
      if (evento.key === 'Escape') {
        cambiar(false);
      }
    });
  }

  function iniciar_reveal() {
    const elementos = document.querySelectorAll('.reveal');
    if (!('IntersectionObserver' in window)) {
      elementos.forEach((el) => el.classList.add('is-visible'));
      return;
    }

    const observador = new IntersectionObserver(
      (entradas) => {
        entradas.forEach((entrada) => {
          if (entrada.isIntersecting) {
            entrada.target.classList.add('is-visible');
            observador.unobserve(entrada.target);
          }
        });
      },
      { rootMargin: '0px 0px -8% 0px', threshold: 0.08 }
    );

    elementos.forEach((el, indice) => {
      el.style.transitionDelay = `${(indice % 3) * 80}ms`;
      observador.observe(el);
    });
  }

  async function cargar_productos() {
    const contenedor = document.getElementById('equipos-listado');
    if (!contenedor) {
      return;
    }

    try {
      const respuesta = await fetch('productos.json', { headers: { Accept: 'application/json' } });
      if (!respuesta.ok) {
        throw new Error('No se pudo cargar el listado');
      }
      const data = await respuesta.json();
      renderizar_productos(contenedor, data);
    } catch {
      contenedor.innerHTML =
        '<p class="equipos-error">No se pudo cargar el listado de equipos. Recarga la página o vuelve más tarde.</p>';
    } finally {
      iniciar_reveal();
    }
  }

  function renderizar_productos(contenedor, data) {
    const categorias = Array.isArray(data.categorias) ? data.categorias : [];
    const productos = Array.isArray(data.productos) ? data.productos : [];
    const fragmentos = [];

    categorias.forEach((categoria) => {
      const items = productos.filter((p) => p.categoria === categoria.id);
      if (!items.length) {
        return;
      }

      fragmentos.push(`
        <section class="equipos-seccion" id="${escapar_attr(categoria.id)}" aria-labelledby="titulo-${escapar_attr(categoria.id)}">
          <header class="section-head section-head-compact reveal">
            <p class="eyebrow">Recomendados</p>
            <h2 id="titulo-${escapar_attr(categoria.id)}">${escapar_html(categoria.titulo)}</h2>
            <p>${escapar_html(categoria.descripcion || '')}</p>
          </header>
          <div class="device-grid">
            ${items.map(tarjeta_producto).join('')}
          </div>
        </section>
      `);
    });

    contenedor.innerHTML = fragmentos.join('') || '<p class="equipos-error">Aún no hay equipos publicados.</p>';
  }

  function tarjeta_producto(producto) {
    const nombre = escapar_html(producto.nombre || 'Equipo');
    const specs = escapar_html(producto.specs || '');
    const tienda = escapar_html(producto.tienda || 'Mercado Libre');
    const url = escapar_attr(producto.url || '#');
    const imagen = escapar_attr(producto.imagen || '');

    return `
      <article class="device-card reveal">
        <a class="device-media" href="${url}" target="_blank" rel="noopener noreferrer" tabindex="-1" aria-hidden="true">
          <img src="${imagen}" alt="" width="400" height="400" loading="lazy" decoding="async">
        </a>
        <div class="device-body">
          <span class="device-store">${tienda}</span>
          <h3>${nombre}</h3>
          <p>${specs}</p>
          <a class="btn btn-primary btn-block" href="${url}" target="_blank" rel="noopener noreferrer">
            Ver en ${tienda}
            <i class="fa-solid fa-arrow-right" aria-hidden="true"></i>
          </a>
        </div>
      </article>
    `;
  }

  function escapar_html(texto) {
    return String(texto)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function escapar_attr(texto) {
    return escapar_html(texto);
  }
})();
