(function () {
  'use strict';

  const URL_LETHORSOFT = 'https://lethorsoft.com';
  const URL_COMPRA = `${URL_LETHORSOFT}/comprar?producto=lethorpos`;
  const URL_PRECIOS = `${URL_LETHORSOFT}/api/precios`;
  const PRODUCTO = 'lethorpos';

  const PERIODOS = {
    mensual: 'mes',
    anual: 'año',
    unico: 'pago único',
    pago_unico: 'pago único'
  };

  document.documentElement.classList.add('js');

  document.addEventListener('DOMContentLoaded', () => {
    aplicar_enlaces();
    iniciar_header();
    iniciar_menu_movil();
    iniciar_reveal();
    cargar_precio();

    const anio = document.getElementById('anio-actual');
    if (anio) {
      anio.textContent = String(new Date().getFullYear());
    }
  });

  function aplicar_enlaces() {
    document.querySelectorAll('[data-compra]').forEach((enlace) => {
      enlace.href = URL_COMPRA;
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
    const logo_hero = document.querySelector('.hero-logo');
    const actualizar = () => {
      header.classList.toggle('is-scrolled', window.scrollY > 24);
      const logo_oculto = !logo_hero || logo_hero.getBoundingClientRect().bottom <= header.offsetHeight;
      header.classList.toggle('logo-visible', logo_oculto);
    };
    actualizar();
    window.addEventListener('scroll', actualizar, { passive: true });
    window.addEventListener('resize', actualizar);
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

  async function cargar_precio() {
    try {
      const respuesta = await fetch(URL_PRECIOS, { headers: { Accept: 'application/json' } });
      if (!respuesta.ok) {
        return;
      }
      const precios = await respuesta.json();
      if (!Array.isArray(precios)) {
        return;
      }
      const precio = precios.find((p) => p && p.producto === PRODUCTO && p.activo !== false);
      if (precio) {
        mostrar_precio(precio);
      }
    } catch {
      // Sin conexión o CORS: se queda el precio de respaldo del HTML.
    }
  }

  function mostrar_precio(precio) {
    const monto = Number(precio.monto);
    if (!Number.isFinite(monto) || monto <= 0) {
      return;
    }

    const moneda = (precio.moneda || 'MXN').toUpperCase();
    const formato = new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      currencyDisplay: 'narrowSymbol',
      minimumFractionDigits: Number.isInteger(monto) ? 0 : 2,
      maximumFractionDigits: 2
    });

    asignar_texto('precio-monto', formato.format(monto));
    asignar_texto('precio-moneda', moneda);
    asignar_texto('precio-periodo', PERIODOS[precio.tipo_cobro] || precio.tipo_cobro || 'mes');
    if (precio.nombre_publico) {
      asignar_texto('precio-nombre', precio.nombre_publico);
    }
  }

  function asignar_texto(id, texto) {
    const el = document.getElementById(id);
    if (el) {
      el.textContent = texto;
    }
  }
})();
