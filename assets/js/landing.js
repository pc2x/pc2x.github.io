(function () {
  'use strict';

  const URL_LETHORSOFT = 'https://lethorsoft.com';
  const URL_COMPRA = `${URL_LETHORSOFT}/comprar?producto=lethorpos`;
  const URL_PRECIOS = `${URL_LETHORSOFT}/api/precios`;
  const PRODUCTO = 'lethorpos';
  const URL_DESCARGA = 'https://github.com/pc2x/Lethorpos-releases/releases/latest';
  const URL_RELEASE_API = 'https://api.github.com/repos/pc2x/Lethorpos-releases/releases/latest';

  const DESCARGAS_POR_SO = {
    windows: {
      icono: 'fa-windows',
      texto: 'Descargar para Windows',
      principal: /^LethorPos-Setup-.*\.exe$/i,
      cliente: /^LethorPos-Cliente-Windows-.*\.exe$/i,
      texto_cliente: 'Cliente ligero para Windows'
    },
    android: {
      icono: 'fa-android',
      texto: 'Descargar para Android',
      principal: /^LethorPos-Android-.*\.apk$/i,
      cliente: /^LethorPos-Cliente-Android-.*\.apk$/i,
      texto_cliente: 'Cliente ligero para Android'
    }
  };

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
    cargar_descarga();

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

  function detectar_so() {
    const agente = navigator.userAgent || '';
    if (/android/i.test(agente)) {
      return 'android';
    }
    if (/iphone|ipad|ipod/i.test(agente) || (/macintosh/i.test(agente) && navigator.maxTouchPoints > 1)) {
      return null;
    }
    if (/windows/i.test(agente)) {
      return 'windows';
    }
    return null;
  }

  async function cargar_descarga() {
    const config = DESCARGAS_POR_SO[detectar_so()];
    if (!config) {
      return;
    }
    try {
      const respuesta = await fetch(URL_RELEASE_API, { headers: { Accept: 'application/vnd.github+json' } });
      if (!respuesta.ok) {
        return;
      }
      const release = await respuesta.json();
      const archivos = Array.isArray(release && release.assets) ? release.assets : [];
      const buscar = (patron) => archivos.find((a) => a && patron.test(a.name));

      const principal = buscar(config.principal);
      if (principal) {
        const boton = document.getElementById('descarga-principal');
        const icono = document.getElementById('descarga-icono');
        if (boton) {
          boton.href = principal.browser_download_url;
          boton.removeAttribute('target');
        }
        if (icono) {
          icono.className = `fa-brands ${config.icono}`;
        }
        asignar_texto('descarga-texto', config.texto);
      }

      const cliente = buscar(config.cliente);
      const enlace_cliente = document.getElementById('descarga-cliente');
      if (cliente && enlace_cliente) {
        enlace_cliente.href = cliente.browser_download_url;
        enlace_cliente.textContent = config.texto_cliente;
        enlace_cliente.hidden = false;
      }
    } catch {
      // Sin conexión o límite de la API: se queda el enlace a la página de descargas.
    }
  }

  function asignar_texto(id, texto) {
    const el = document.getElementById(id);
    if (el) {
      el.textContent = texto;
    }
  }
})();
