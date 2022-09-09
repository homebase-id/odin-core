"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[95],{9072:function(e,t,n){n.d(t,{Z:function(){return g},s:function(){return h}});var s=n(885),r=n(1413),a=n(2791),i=n(8923),l=n(4665),c=n(4648),o=n(1088),u=n(184);n(1025);var d=function(e){var t=e.className;return(0,u.jsxs)("div",{className:"loader ".concat(t),children:[(0,u.jsx)("div",{}),(0,u.jsx)("div",{}),(0,u.jsx)("div",{}),(0,u.jsx)("div",{})]})},m=n(7804),f=n(5207),x=function(e){var t=e.className;return(0,u.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,u.jsx)("path",{d:"M433.1 129.1l-83.9-83.9C342.3 38.32 327.1 32 316.1 32H64C28.65 32 0 60.65 0 96v320c0 35.35 28.65 64 64 64h320c35.35 0 64-28.65 64-64V163.9C448 152.9 441.7 137.7 433.1 129.1zM224 416c-35.34 0-64-28.66-64-64s28.66-64 64-64s64 28.66 64 64S259.3 416 224 416zM320 208C320 216.8 312.8 224 304 224h-224C71.16 224 64 216.8 64 208v-96C64 103.2 71.16 96 80 96h224C312.8 96 320 103.2 320 112V208z"})})},v=n(7134),h=function(e,t){return"error"===e||"error"===t?"error":"loading"===e||"loading"===t?"loading":"idle"===e&&"idle"===t?"idle":"success"===e&&"success"===t||"success"===e&&"idle"===t||"idle"===e&&"success"===t?"success":void 0},g=function(e){var t=e.children,n=e.onClick,h=e.className,g=e.icon,p=e.type,w=e.state,b=e.title,j=e.size,N=e.confirmOptions,C=function(e){return"loading"===w?(0,u.jsx)(d,(0,r.Z)({},e)):"success"===w?(0,u.jsx)(c.Z,(0,r.Z)({},e)):"error"===w?(0,u.jsx)(o.Z,(0,r.Z)({},e)):"save"===g?(0,u.jsx)(x,(0,r.Z)({},e)):"send"===g?(0,u.jsx)(l.Z,(0,r.Z)({},e)):"plus"===g?(0,u.jsx)(f.Z,(0,r.Z)({},e)):"trash"===g?(0,u.jsx)(v.Z,(0,r.Z)({},e)):"edit"===g?(0,u.jsx)(m.Z,(0,r.Z)({},e)):"up"===g?(0,u.jsx)(l.Z,(0,r.Z)((0,r.Z)({},e),{},{className:"-rotate-90 ".concat(e.className)})):"down"===g?(0,u.jsx)(l.Z,(0,r.Z)((0,r.Z)({},e),{},{className:"rotate-90 ".concat(e.className)})):(0,u.jsx)(u.Fragment,{})},y=(0,a.useState)(!1),Z=(0,s.Z)(y,2),k=Z[0],L=Z[1],z="error"===w?"bg-red-500 hover:bg-red-600 text-white":"secondary"===p?"bg-slate-100 hover:bg-slate-200 dark:bg-slate-700 dark:hover:bg-slate-800 dark:text-white":"remove"===p?"bg-red-200 hover:bg-red-400 dark:bg-red-700 hover:dark:bg-red-800 dark:text-white":"bg-green-500 hover:bg-green-600 text-white",M=t?"min-w-[6rem] ".concat(null!==h&&void 0!==h&&h.indexOf("w-full")?"":"w-full sm:w-auto"):"",S="large"===j?"px-5 py-3":"small"===j?"px-3 py-1 text-sm":"px-3 py-2",V="loading"===w?"animate-pulse":"";return(0,u.jsxs)(u.Fragment,{children:[(0,u.jsxs)("button",{className:"flex flex-row rounded-md text-left ".concat(M," ").concat(S," ").concat(z," ").concat(V," ").concat(h),disabled:"loading"===w,onClick:N?function(e){return e.preventDefault(),L(!0),!1}:n,title:b,children:[t&&(0,u.jsx)("span",{className:"mr-1",children:t}),(0,u.jsx)(C,{className:"my-auto ml-auto h-4 w-4"})]}),N&&(0,u.jsx)(i.Z,{title:N.title,confirmText:N.buttonText,needConfirmation:k,onConfirm:n,onCancel:function(){L(!1)},children:(0,u.jsx)("p",{className:"text-sm text-gray-500",children:N.body})})]})}},5660:function(e,t,n){var s=n(885),r=n(2791),a=n(184);t.Z=function(e){var t=e.className,n=e.state,i=(0,r.useState)(null),l=(0,s.Z)(i,2),c=l[0],o=l[1],u=(0,r.useState)(new Date),d=(0,s.Z)(u,2),m=d[0],f=d[1];if((0,r.useEffect)((function(){"success"===n&&o(new Date)}),[n]),(0,r.useEffect)((function(){var e=setTimeout((function(){f(new Date)}),3e4);return function(){clearTimeout(e)}}),[m]),!c)return null;var x=m.getTime()-c.getTime(),v=c?x<=6e4?"a few seconds ago":x<=6e5?"a few minutes ago":c.toLocaleString():"";return v?(0,a.jsxs)("p",{className:"".concat(t," text-sm"),children:["Last saved ",v]}):null}},6123:function(e,t,n){n.d(t,{Z:function(){return l}});var s=n(2791),r=n(1261),a=n(184),i=function(e){var t=e.className;return(0,a.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 320 512",fill:"currentColor",className:t,children:(0,a.jsx)("path",{d:"M310.6 150.6c12.5-12.5 12.5-32.8 0-45.3s-32.8-12.5-45.3 0L160 210.7 54.6 105.4c-12.5-12.5-32.8-12.5-45.3 0s-12.5 32.8 0 45.3L114.7 256 9.4 361.4c-12.5 12.5-12.5 32.8 0 45.3s32.8 12.5 45.3 0L160 301.3 265.4 406.6c12.5 12.5 32.8 12.5 45.3 0s12.5-32.8 0-45.3L205.3 256 310.6 150.6z"})})},l=function(e){var t=e.children,n=e.title,l=e.onClose,c=e.size,o=void 0===c?"large":c,u=(0,s.useRef)(null);return(0,r.Z)(u,(function(){return l&&l()})),(0,a.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,a.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,a.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,a.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,a.jsx)("div",{ref:u,className:"relative transform rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full ".concat("normal"===o?"sm:max-w-lg":"large"===o?"sm:max-w-xl":"sm:max-w-4xl"),children:(0,a.jsxs)("div",{className:"bg-white px-8 py-8 dark:bg-black dark:text-slate-50",children:[n||l?(0,a.jsxs)("div",{className:"-mx-8 -mt-8 mb-8 flex flex-row bg-slate-100 px-8 py-4 dark:bg-slate-700",children:[n?(0,a.jsx)("h3",{className:"my-3 text-2xl font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:n}):null,l?(0,a.jsx)("button",{onClick:l,className:"ml-auto p-2",children:(0,a.jsx)(i,{className:"h-5 w-5"})}):null]}):null,t]})})})})]})}},8923:function(e,t,n){var s=n(4164),r=n(4990),a=n(3412),i=n(1088),l=n(184);t.Z=function(e){var t=e.title,n=e.confirmText,c=e.children,o=e.needConfirmation,u=e.onConfirm,d=e.onCancel,m=(0,a.Z)("modal-container");if(!o)return null;var f=(0,l.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,l.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,l.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",onClick:d,children:(0,l.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,l.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",onClick:function(e){return e.preventDefault(),!1},children:[(0,l.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,l.jsxs)("div",{className:"sm:flex sm:items-start",children:[(0,l.jsx)("div",{className:"mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full text-red-400 sm:mx-0 sm:h-10 sm:w-10",children:(0,l.jsx)(i.Z,{})}),(0,l.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,l.jsx)("h3",{className:"text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,l.jsx)("div",{className:"mt-2",children:c})]})]})}),(0,l.jsxs)("div",{className:"bg-gray-50 px-4 py-3 dark:bg-slate-900 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,l.jsx)("button",{type:"button",className:"inline-flex w-full justify-center rounded-md border border-transparent bg-red-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 sm:ml-3 sm:w-auto sm:text-sm",onClick:u,children:n}),(0,l.jsx)("button",{type:"button",className:"mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:border-gray-800 dark:bg-slate-700 dark:text-white sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm",onClick:d,children:(0,r.t)("Cancel")})]})]})})})]});return(0,s.createPortal)(f,m)}},4648:function(e,t,n){var s=n(184);t.Z=function(e){var t=e.className;return(0,s.jsxs)("svg",{fill:"none",stroke:"currentColor",strokeLinecap:"round",strokeLinejoin:"round",strokeWidth:"3",className:t,viewBox:"0 0 24 24",children:[(0,s.jsx)("path",{d:"M22 11.08V12a10 10 0 11-5.93-9.14"}),(0,s.jsx)("path",{d:"M22 4L12 14.01l-3-3"})]})}},1088:function(e,t,n){var s=n(184);t.Z=function(e){var t=e.className;return(0,s.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,s.jsx)("path",{d:"M256 0C114.6 0 0 114.6 0 256s114.6 256 256 256s256-114.6 256-256S397.4 0 256 0zM232 152C232 138.8 242.8 128 256 128s24 10.75 24 24v128c0 13.25-10.75 24-24 24S232 293.3 232 280V152zM256 400c-17.36 0-31.44-14.08-31.44-31.44c0-17.36 14.07-31.44 31.44-31.44s31.44 14.08 31.44 31.44C287.4 385.9 273.4 400 256 400z"})})}},7804:function(e,t,n){var s=n(184);t.Z=function(e){var t=e.className;return(0,s.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,s.jsx)("path",{d:"M421.7 220.3L188.5 453.4L154.6 419.5L158.1 416H112C103.2 416 96 408.8 96 400V353.9L92.51 357.4C87.78 362.2 84.31 368 82.42 374.4L59.44 452.6L137.6 429.6C143.1 427.7 149.8 424.2 154.6 419.5L188.5 453.4C178.1 463.8 165.2 471.5 151.1 475.6L30.77 511C22.35 513.5 13.24 511.2 7.03 504.1C.8198 498.8-1.502 489.7 .976 481.2L36.37 360.9C40.53 346.8 48.16 333.9 58.57 323.5L291.7 90.34L421.7 220.3zM492.7 58.75C517.7 83.74 517.7 124.3 492.7 149.3L444.3 197.7L314.3 67.72L362.7 19.32C387.7-5.678 428.3-5.678 453.3 19.32L492.7 58.75z"})})}},5207:function(e,t,n){var s=n(184);t.Z=function(e){var t=e.className;return(0,s.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",className:t,fill:"currentColor",viewBox:"0 0 448 512",children:(0,s.jsx)("path",{d:"M432 256c0 17.69-14.33 32.01-32 32.01H256v144c0 17.69-14.33 31.99-32 31.99s-32-14.3-32-31.99v-144H48c-17.67 0-32-14.32-32-32.01s14.33-31.99 32-31.99H192v-144c0-17.69 14.33-32.01 32-32.01s32 14.32 32 32.01v144h144C417.7 224 432 238.3 432 256z"})})}},7134:function(e,t,n){var s=n(184);t.Z=function(e){var t=e.className;return(0,s.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,s.jsx)("path",{d:"M135.2 17.69C140.6 6.848 151.7 0 163.8 0H284.2C296.3 0 307.4 6.848 312.8 17.69L320 32H416C433.7 32 448 46.33 448 64C448 81.67 433.7 96 416 96H32C14.33 96 0 81.67 0 64C0 46.33 14.33 32 32 32H128L135.2 17.69zM31.1 128H416V448C416 483.3 387.3 512 352 512H95.1C60.65 512 31.1 483.3 31.1 448V128zM111.1 208V432C111.1 440.8 119.2 448 127.1 448C136.8 448 143.1 440.8 143.1 432V208C143.1 199.2 136.8 192 127.1 192C119.2 192 111.1 199.2 111.1 208zM207.1 208V432C207.1 440.8 215.2 448 223.1 448C232.8 448 240 440.8 240 432V208C240 199.2 232.8 192 223.1 192C215.2 192 207.1 199.2 207.1 208zM304 208V432C304 440.8 311.2 448 320 448C328.8 448 336 440.8 336 432V208C336 199.2 328.8 192 320 192C311.2 192 304 199.2 304 208z"})})}},3004:function(e,t,n){var s=n(3504),r=n(5660),a=n(184);t.Z=function(e){var t=e.title,n=e.actions,i=e.saveStatus,l=e.breadCrumbs,c=e.icon;return(0,a.jsx)("section",{className:"-my-8 -mx-10 mb-10 border-b-2 border-gray-100 bg-slate-50 py-8 px-10 dark:border-gray-700 dark:bg-slate-800",children:(0,a.jsxs)("div",{className:"flex flex-row",children:[(0,a.jsxs)("div",{className:"flex-col",children:[l&&(0,a.jsx)("ul",{className:"mb-2 hidden flex-row sm:flex",children:l.map((function(e,t){return(0,a.jsx)("li",{className:"mr-2",children:e.href?(0,a.jsxs)(s.rU,{to:e.href,className:"",children:[e.title,(0,a.jsx)("span",{className:"ml-2",children:">"})]}):(0,a.jsx)("span",{className:"text-slate-500",children:e.title})},t)}))}),t&&(0,a.jsxs)("h1",{className:"mb-5 flex flex-row text-4xl dark:text-white",children:[c&&c({className:"h-8 w-8 my-auto mr-4"})," ",t]})]}),(0,a.jsxs)("div",{className:"ml-auto ",children:[(0,a.jsx)("div",{className:"flex flex-row",children:n}),i&&(0,a.jsx)(r.Z,{className:"mt-1",state:i})]})]})})}},8808:function(e,t,n){var s=n(3504),r=n(4665),a=n(715),i=n(184);t.Z=function(e){var t=e.circleDef,n=e.permissionLevel;return t?(0,i.jsx)("div",{className:"mb-4 flex flex-row",children:(0,i.jsxs)(s.rU,{to:"/owner/circles/".concat(encodeURIComponent(t.id)),className:"flex flex-row hover:text-slate-700 hover:underline dark:hover:text-slate-400",children:[(0,i.jsx)(a.Z,{className:"mt-1 mb-auto mr-3 h-6 w-6"}),(0,i.jsx)("div",{className:"mr-2 flex flex-col",children:(0,i.jsxs)("p",{className:"my-auto leading-none",children:[null===t||void 0===t?void 0:t.name,n&&": ".concat(n)]})}),(0,i.jsx)(r.Z,{className:"my-auto ml-auto h-5 w-5"})]})}):(0,i.jsx)(i.Fragment,{})}},7266:function(e,t,n){n.d(t,{RG:function(){return i},fP:function(){return a},hz:function(){return l},m3:function(){return c},rS:function(){return s},vr:function(){return r}});var s=function(e){return e[Math.floor(Math.random()*e.length)]},r=function(){return s(["Ut bibendum, neque ac lacinia aliquam, justo ipsum aliquam urna, id vestibulum augue mauris sit amet lacus.","Proin ante sapien, interdum sit amet eros sit amet, eleifend pharetra metus.","Sed elit mi, euismod eget neque at, suscipit aliquam nisi.","Nunc diam arcu, tincidunt quis dignissim ac, volutpat non odio."])},a=function(){var e=["adorable","beautiful","clean","drab","elegant","fancy","glamorous","handsome","long","magnificent","old-fashioned","plain","quaint","sparkling","ugliest","unsightly","angry","bewildered","clumsy","defeated","embarrassed","fierce","grumpy","helpless","itchy","jealous","lazy","mysterious","nervous","obnoxious","panicky","repulsive","scary","thoughtless","uptight","worried"];return"".concat(s(e)," ").concat(s(e)," ").concat(s(["women","men","children","teeth","feet","people","leaves","mice","geese","halves","knives","wives","lives","elves","loaves","potatoes","tomatoes","cacti","foci","fungi","nuclei","syllabuses","analyses","diagnoses","oases","theses","crises","phenomena","criteria","data"]))},i=function(e){for(var t=window.atob(e),n=t.length,s=new Uint8Array(n),r=0;r<n;r++)s[r]=t.charCodeAt(r);return s.buffer},l=function(e,t){return t.reduce((function(t,n){return n.value>t.value&&n.value<=e?n:t}),t[0])},c=function(e,t,n){var s=e[t];return e.splice(t,1),e.splice(n,0,s),e}},1261:function(e,t,n){var s=n(2791);t.Z=function(e,t){(0,s.useEffect)((function(){function n(n){e.current&&!e.current.contains(n.target)&&t()}return document.addEventListener("mousedown",n),function(){document.removeEventListener("mousedown",n)}}),[e])}},1512:function(e,t,n){var s=n(4165),r=n(5861),a=n(7408),i=n(1803),l=n(6117);t.Z=function(e){var t=e.targetDrive,n=(0,l.Z)().getSharedSecret,c=new i.AO({api:i.Ii.Owner,sharedSecret:n()}),o=function(){var e=(0,r.Z)((0,s.Z)().mark((function e(t){var n;return(0,s.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,c.driveProvider.GetDrives({pageNumber:1,pageSize:100});case 2:return e.next=4,e.sent.results;case 4:return n=e.sent,e.abrupt("return",n.find((function(e){return e.targetDriveInfo.alias===t.alias&&e.targetDriveInfo.type===t.type})));case 6:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return{fetch:(0,a.useQuery)(["drive","".concat(t.alias,"_").concat(t.type)],(function(){return o(t)}),{refetchOnWindowFocus:!1})}}},3412:function(e,t,n){var s=n(2791);t.Z=function(e){var t=(0,s.useRef)(null);return(0,s.useEffect)((function(){var n,s=document.querySelector("#".concat(e)),r=s||function(e){var t=document.createElement("div");return t.setAttribute("id",e),t}(e);return s||(n=r,document.body.insertBefore(n,document.body.lastElementChild.nextElementSibling)),r.appendChild(t.current),function(){t.current.remove(),r.childElementCount||r.remove()}}),[e]),t.current||(t.current=document.createElement("div")),t.current}},2562:function(e,t,n){n.d(t,{A:function(){return r},W:function(){return a}});var s=n(4990),r=[{name:(0,s.t)("None"),value:0},{name:(0,s.t)("Reader"),value:1},{name:(0,s.t)("Editor"),value:3}],a=[{name:(0,s.t)("None"),value:0},{name:(0,s.t)("Read Connections"),value:10},{name:(0,s.t)("Read Connection Requests"),value:30},{name:(0,s.t)("Read Circle Members"),value:50}]},1025:function(e,t,n){n.r(t),t.default={}}}]);
//# sourceMappingURL=95.594d8b3b.chunk.js.map