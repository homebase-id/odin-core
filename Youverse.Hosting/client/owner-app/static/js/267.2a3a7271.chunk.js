"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[267],{3225:function(e,t,n){var r=n(1413),i=n(184);t.Z=function(e){return(0,i.jsx)("select",(0,r.Z)((0,r.Z)({},e),{},{className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className),children:e.children}))}},8491:function(e,t,n){var r=n(184);t.Z=function(e){var t=e.title,n=e.className,i=e.children,a=e.isOpaqueBg,o=void 0!==a&&a;return(0,r.jsxs)("section",{className:"my-5 rounded-md ".concat(o?"rounded-lg border-[1px] border-gray-200 border-opacity-80 dark:border-gray-700":"bg-slate-50 dark:bg-slate-800"," px-5  dark:text-slate-300 ").concat(null!==n&&void 0!==n?n:""),children:[t?(0,r.jsx)("div",{className:"relative border-b-[1px] border-gray-200 border-opacity-80 py-5 transition-all duration-300 dark:border-gray-700",children:(0,r.jsx)("h3",{className:"text-2xl dark:text-white",children:t})}):null,(0,r.jsx)("div",{className:"py-5 ",children:i})]})}},3391:function(e,t,n){n.r(t),n.d(t,{default:function(){return H}});var r=n(1413),i=n(2982),a=n(885),o=n(2791),s=n(6871),c=n(4165),l=n(5861),u=n(7408),d=n(6752),f=n(6117),m=function(e){var t=e.profileId,n=e.sectionId,i=(0,f.Z)().getSharedSecret,a=new d.KU({api:d.Ii.Owner,sharedSecret:i()}),o=function(){var e=(0,l.Z)((0,c.Z)().mark((function e(t,n){var i;return(0,c.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,a.profileDataProvider.getProfileAttributes(t,n,1,100);case 2:return i=e.sent,e.abrupt("return",i.map((function(e){return(0,r.Z)((0,r.Z)({},e),{},{typeDefinition:Object.values(d.H1).find((function(t){return t.type.toString()===e.type}))})})));case 4:case"end":return e.stop()}}),e)})));return function(t,n){return e.apply(this,arguments)}}();return[(0,u.useQuery)(["attributes",t,n],(function(){return o(t,n)}),{refetchOnMount:!1,refetchOnWindowFocus:!1})]},p=n(3431),h=n(184),v=function(e){var t=e.className,n=e.items,r=e.onChange;return(0,h.jsx)("div",{className:"flex ".concat(t),children:n.map((function(e){var t;return(0,h.jsx)("a",{className:"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(e.isActive?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(t=e.className)&&void 0!==t?t:""),onClick:function(){r(e.key)},children:e.title},e.key)}))})},x=n(4990),b=n(9077),g=n(8191),y=n(5660),j=n(2379),Z=n(8491),w=n(613),N=n(2878),O=n(6916),S=n(2465),k=function(e){var t=e.attribute,n=e.onChange,r=(0,o.useMemo)((function(){return(0,N.Z)(n,500)}),[n]);switch(t.type){case d.H1.Name.type.toString():return(0,h.jsxs)("div",{className:"-mx-2 flex flex-row",children:[(0,h.jsxs)("div",{className:"mb-5 w-2/5 px-2",children:[(0,h.jsx)("label",{htmlFor:"given-name",children:(0,x.t)("Given name")}),(0,h.jsx)(O.Z,{id:"given-name",name:"givenName",defaultValue:t.data.givenName,onChange:r})]}),(0,h.jsxs)("div",{className:"mb-5 w-3/5 px-2",children:[(0,h.jsx)("label",{htmlFor:"sur-name",children:(0,x.t)("Surname")}),(0,h.jsx)(O.Z,{id:"sur-name",name:"surname",defaultValue:t.data.surname,onChange:r})]})]});case d.H1.InstagramUsername.type.toString():case d.H1.TiktokUsername.type.toString():case d.H1.TwitterUsername.type.toString():case d.H1.LinkedInUsername.type.toString():case d.H1.FacebookUsername.type.toString():return(0,h.jsx)(h.Fragment,{children:(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"handle",children:t.typeDefinition.name}),(0,h.jsx)(O.Z,{id:"handle",name:t.typeDefinition.name.toLowerCase(),defaultValue:t.data[t.typeDefinition.name.toLowerCase()],onChange:r})]})});case d.H1.FullBio.type.toString():return(0,h.jsxs)(h.Fragment,{children:[(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"short-bio",children:(0,x.t)("Bio")}),(0,h.jsx)(O.Z,{id:"short-bio",name:"short_bio",defaultValue:t.data.short_bio,onChange:r})]}),(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"full-bio",children:(0,x.t)("Full bio")}),(0,h.jsx)(S.Z,{id:"full-bio",name:"full_bio",defaultValue:t.data.full_bio,onChange:r})]})]});case d.H1.CreditCard.type.toString():return(0,h.jsxs)(h.Fragment,{children:[(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"cc-alias",children:(0,x.t)("Alias")}),(0,h.jsx)(O.Z,{id:"cc-alias",name:"cc_alias",defaultValue:t.data.cc_alias,onChange:r})]}),(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"cc-name",children:(0,x.t)("Name on Card")}),(0,h.jsx)(O.Z,{id:"cc-name",name:"cc_name",defaultValue:t.data.cc_name,onChange:r})]}),(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"cc-number",children:(0,x.t)("Credit card number")}),(0,h.jsx)(O.Z,{id:"cc-number",name:"cc_number",defaultValue:t.data.cc_number,onChange:r})]})]});default:return(0,h.jsx)(h.Fragment,{children:Object.keys(t.data).map((function(e){return(0,h.jsxs)("p",{className:"whitespace-pre-line",children:[e,": ",t.data[e]]},e)}))})}},C=function(e){var t=e.attribute,n=e.className,i=(0,o.useState)(!1),s=(0,a.Z)(i,2),c=s[0],l=s[1],u=(0,b.Z)({}),d=(0,a.Z)(u,3),f=d[1],m=f.mutate,p=f.status,v=f.isLoading,N=f.isError,O=f.isSuccess,S=d[2].mutate,C=(0,r.Z)({},t.data),I=function(){m((0,r.Z)((0,r.Z)({},t),{},{data:C}))};return(0,h.jsxs)(h.Fragment,{children:[(0,h.jsxs)(Z.Z,{isOpaqueBg:!0,title:(0,h.jsxs)(h.Fragment,{children:[(0,h.jsx)(w.Z,{acl:t.acl})," ",t.typeDefinition.name]}),className:"".concat(n," relative"),children:[(0,h.jsx)(k,{attribute:t,onChange:function(e){C[e.target.name]=e.target.value,I()}}),(0,h.jsxs)("div",{className:"top-5 right-5 flex flex-row md:absolute",children:[(0,h.jsx)(g.Z,{type:"remove",icon:"trash",className:"ml-auto",onClick:function(){l(!0)}}),(0,h.jsx)(g.Z,{state:v?"loading":O?"success":N?"failed":"success",type:"primary",className:"ml-2",onClick:I,children:(0,x.t)("Save")})]}),(0,h.jsx)(y.Z,{className:"mt-2 text-right sm:mt-0",state:p})]}),(0,h.jsx)(j.Z,{title:"Remove Attribute",confirmText:"Permanently remove",needConfirmation:c,onConfirm:function(){l(!1),S(t)},onCancel:function(){l(!1)},children:(0,h.jsxs)("p",{className:"text-sm text-gray-500",children:[(0,x.t)("Are you sure you want to remove your")," ",t.typeDefinition.name," ",(0,x.t)("attribute. This action cannot be undone.")]})})]})},I=n(3225),F=function(e){var t,n=e.profileId,i=e.sectionId,s=(0,o.useState)(!1),c=(0,a.Z)(s,2),l=c[0],u=c[1],f=(0,o.useState)(),m=(0,a.Z)(f,2),p=m[0],v=m[1],y=(0,b.Z)({}),j=(0,a.Z)(y,2)[1],w=j.mutate,N=j.isLoading,O=j.isError,S=j.isSuccess,C=function(){u(!1),v(void 0)};return(0,h.jsx)(h.Fragment,{children:l?(0,h.jsxs)(Z.Z,{title:"New".concat(p?":":""," ").concat(null!==(t=null===p||void 0===p?void 0:p.typeDefinition.name)&&void 0!==t?t:""),isOpaqueBg:!0,children:[void 0===p?(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"type",children:(0,x.t)("Attribute Type")}),(0,h.jsxs)(I.Z,{id:"type",onChange:function(e){!function(e){var t=Object.values(d.H1).find((function(t){return t.type.toString()===e}));v({id:"",type:e,sectionId:i,priority:-1,data:{},typeDefinition:t,profileId:n,acl:{requiredSecurityGroup:d.hh.Owner}})}(e.target.value)},children:[(0,h.jsx)("option",{children:(0,x.t)("Make a selection")}),Object.values(d.H1).map((function(e){return(0,h.jsx)("option",{value:e.type.toString(),children:e.name},e.type.toString())}))]})]}):(0,h.jsx)(k,{attribute:p,onChange:function(e){if(p){var t=(0,r.Z)({},p);t.data[e.target.name]=e.target.value,v(t)}}}),(0,h.jsxs)("div",{className:"flex flex-row",children:[(0,h.jsx)(g.Z,{type:"secondary",className:"ml-auto",onClick:C,children:(0,x.t)("Cancel")}),(0,h.jsx)(g.Z,{type:"primary",icon:"plus",className:"ml-2",onClick:function(){console.log(p),w(p,{onSuccess:function(){C()}})},state:N?"loading":S?"success":O?"failed":void 0,children:(0,x.t)("Add")})]})]}):(0,h.jsx)("div",{className:"flex flex-row",children:(0,h.jsx)(g.Z,{type:"primary",icon:"plus",className:"mx-auto min-w-[9rem]",onClick:function(){return u(!0)},children:(0,x.t)("Add Attribute")})})})},D=n(5207),P=function(e){var t=e.className;return(0,h.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,h.jsx)("path",{d:"M215.1 272h-136c-12.94 0-24.63 7.797-29.56 19.75C45.47 303.7 48.22 317.5 57.37 326.6l30.06 30.06l-78.06 78.07c-12.5 12.5-12.5 32.75-.0012 45.25l22.62 22.62c12.5 12.5 32.76 12.5 45.26 .0013l78.06-78.07l30.06 30.06c6.125 6.125 14.31 9.367 22.63 9.367c4.125 0 8.279-.7891 12.25-2.43c11.97-4.953 19.75-16.62 19.75-29.56V296C239.1 282.7 229.3 272 215.1 272zM296 240h136c12.94 0 24.63-7.797 29.56-19.75c4.969-11.97 2.219-25.72-6.938-34.87l-30.06-30.06l78.06-78.07c12.5-12.5 12.5-32.76 .0002-45.26l-22.62-22.62c-12.5-12.5-32.76-12.5-45.26-.0003l-78.06 78.07l-30.06-30.06c-9.156-9.141-22.87-11.84-34.87-6.937c-11.97 4.953-19.75 16.62-19.75 29.56v135.1C272 229.3 282.7 240 296 240z"})})},T=n(3004),A=function(e){var t=e.profileDefinition,n=(0,o.useState)(""),s=(0,a.Z)(n,2),c=s[0],l=s[1];return(0,h.jsx)(Z.Z,{title:"New: section",isOpaqueBg:!0,children:(0,h.jsxs)("form",{onSubmit:function(e){e.preventDefault();var n={sectionId:"",attributes:[],priority:Math.max.apply(Math,(0,i.Z)(t.sections.map((function(e){return e.priority}))))+1,isSystemSection:!1,name:c},a=(0,r.Z)({},t);return a.sections.push(n),console.log("Should create: ",a),!1},children:[(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"name",children:(0,x.t)("Name")}),(0,h.jsx)(O.Z,{id:"name",name:"sectionName",onChange:function(e){l(e.target.value)},required:!0})]}),(0,h.jsx)("div",{className:"flex flex-row",children:(0,h.jsx)(g.Z,{className:"ml-auto",children:(0,x.t)("add section")})})]})})},M=function(e){var t=e.section,n=e.profileId,r=m({profileId:n,sectionId:t.sectionId}),o=(0,a.Z)(r,1)[0],s=o.data,c=o.isLoading;if(!s||c)return(0,h.jsx)(h.Fragment,{children:"Loading"});var l=s.reduce((function(e,t){return-1!==e.indexOf(t.type)?e:[].concat((0,i.Z)(e),[t.type])}),[]).map((function(e){var t=s.filter((function(t){return t.type===e}));return{name:t[0].typeDefinition.name,attributes:t}}));return(0,h.jsxs)(h.Fragment,{children:[s.length?l.map((function(e){return(0,h.jsx)(_,{groupTitle:e.name,attributes:e.attributes},e.name)})):(0,h.jsx)("div",{className:"py-5",children:(0,x.t)("section-empty-attributes")}),(0,h.jsx)(F,{profileId:n,sectionId:t.sectionId})]})},_=function(e){var t=e.attributes,n=e.groupTitle,r=(0,o.useState)(1===t.length),i=(0,a.Z)(r,2),s=i[0],c=i[1];return 1===t.length?(0,h.jsx)(C,{attribute:t[0]}):(0,h.jsxs)("div",{className:"relative my-10 overflow-x-hidden ".concat(s?"":"cursor-pointer transition-transform"),style:{paddingBottom:"".concat(10*t.length,"px")},onClick:function(){s||c(!0)},children:[(0,h.jsxs)("h2",{onClick:function(){return c(!1)},className:"cursor-pointer text-2xl",children:[(0,h.jsx)(P,{className:"inline-block h-4 w-4 ".concat(s?"opacity-100":"opacity-0")})," ",n," ",(0,h.jsxs)("small",{children:["(",t.length,")"]})]}),(0,h.jsx)("div",{className:"border-l-[16px] border-slate-50 pt-5 transition-transform dark:border-slate-600 ".concat(s?"pl-5":"-translate-x-4 hover:translate-x-0"),children:t.map((function(e,t){return(0,h.jsx)("span",{className:s||0===t?"":"absolute left-0 right-0 top-0 bg-white shadow-slate-50 dark:bg-slate-800",style:{transform:"translateX(".concat(4*t,"px) translateY(").concat(10*t,"px)")},children:(0,h.jsx)(C,{attribute:e,className:s?"mt-0 mb-5":"pointer-events-none my-0 opacity-50 grayscale"})},e.id)}))})]})},H=function(){var e=(0,p.Z)(),t=e.data,n=e.isLoading,r=(0,s.UO)().profileKey,c=null===t||void 0===t?void 0:t.definitions.find((function(e){return e.slug===r})),l=(0,o.useState)(null!==c&&void 0!==c&&c.sections?c.sections[0].sectionId:""),u=(0,a.Z)(l,2),d=u[0],f=u[1];if(n)return(0,h.jsx)(h.Fragment,{children:"Loading"});if(!t)return(0,h.jsx)(h.Fragment,{children:(0,x.t)("no-data-found")});if(!c)return(0,h.jsx)(h.Fragment,{children:"Incorrect profile path"});var m="new"===d?void 0:c.sections.find((function(e){return e.sectionId===d}))||c.sections[0];return(0,h.jsxs)(h.Fragment,{children:[(0,h.jsx)(T.Z,{title:c.name}),(0,h.jsx)(v,{className:"mt-5",items:[].concat((0,i.Z)(c.sections.map((function(e,t){return{title:e.name,key:e.sectionId,isActive:d?d===e.sectionId:0===t}}))),[{title:(0,h.jsx)(D.Z,{className:"h-5 w-5"}),key:"new",isActive:d?"new"===d:!c.sections.length,className:"flex-grow-0"}]),onChange:function(e){f(e)}}),"new"===d?(0,h.jsx)(A,{profileDefinition:c}):m&&(0,h.jsx)(M,{section:m,profileId:c.profileId},m.sectionId)]})}},9077:function(e,t,n){var r=n(4165),i=n(5861),a=n(7408),o=n(6752),s=n(6117);t.Z=function(e){var t=e.profileId,n=e.attributeId,c=(0,a.useQueryClient)(),l=(0,s.Z)().getSharedSecret,u=new o.KU({api:o.Ii.Owner,sharedSecret:l()}),d=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(t,n){var i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(t&&n){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.profileDataProvider.getAttribute(t,n);case 4:return i=e.sent,e.abrupt("return",i);case 6:case"end":return e.stop()}}),e)})));return function(t,n){return e.apply(this,arguments)}}(),f=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(t){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,u.profileDataProvider.saveAttribute(t);case 2:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),m=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(t){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(!t.fileId){e.next=5;break}return e.next=3,u.profileDataProvider.removeAttribute(t.sectionId,t.fileId);case 3:e.next=6;break;case 5:console.log("error...");case 6:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return[(0,a.useQuery)(["attribute",t,n],(function(){return d(t,n)}),{refetchOnMount:!1,refetchOnWindowFocus:!1}),(0,a.useMutation)(f,{onSuccess:function(e,t){t.id?c.invalidateQueries(["attribute",t.profileId,t.id]):c.invalidateQueries(["attribute"]),c.removeQueries(["attributes",t.profileId,t.sectionId])}}),(0,a.useMutation)(m,{onSuccess:function(e,t){t.id?c.invalidateQueries(["attribute",t.profileId,t.id]):c.invalidateQueries(["attribute"]),c.removeQueries(["attributes",t.profileId,t.sectionId])}})]}},3431:function(e,t,n){var r=n(4165),i=n(1413),a=n(5861),o=n(7408),s=n(6752),c=n(3990),l=n(6117);t.Z=function(){var e=(0,l.Z)().getSharedSecret,t=function(){var t=(0,a.Z)((0,r.Z)().mark((function t(){var n,a;return(0,r.Z)().wrap((function(t){for(;;)switch(t.prev=t.next){case 0:return n=new s.KU({api:s.Ii.Owner,sharedSecret:e()}),t.next=3,n.profileDefinitionProvider.getProfileDefinitions();case 3:return t.next=5,t.sent.map((function(e){return(0,i.Z)((0,i.Z)({},e),{},{slug:(0,c.V)(e.name)})}));case 5:return a=t.sent,t.abrupt("return",{definitions:a});case 7:case"end":return t.stop()}}),t)})));return function(){return t.apply(this,arguments)}}();return(0,o.useQuery)(["profiles"],(function(){return t()}),{refetchOnMount:!1,refetchOnWindowFocus:!1})}},4942:function(e,t,n){function r(e,t,n){return t in e?Object.defineProperty(e,t,{value:n,enumerable:!0,configurable:!0,writable:!0}):e[t]=n,e}n.d(t,{Z:function(){return r}})},1413:function(e,t,n){n.d(t,{Z:function(){return a}});var r=n(4942);function i(e,t){var n=Object.keys(e);if(Object.getOwnPropertySymbols){var r=Object.getOwnPropertySymbols(e);t&&(r=r.filter((function(t){return Object.getOwnPropertyDescriptor(e,t).enumerable}))),n.push.apply(n,r)}return n}function a(e){for(var t=1;t<arguments.length;t++){var n=null!=arguments[t]?arguments[t]:{};t%2?i(Object(n),!0).forEach((function(t){(0,r.Z)(e,t,n[t])})):Object.getOwnPropertyDescriptors?Object.defineProperties(e,Object.getOwnPropertyDescriptors(n)):i(Object(n)).forEach((function(t){Object.defineProperty(e,t,Object.getOwnPropertyDescriptor(n,t))}))}return e}},2878:function(e,t,n){n.d(t,{Z:function(){return D}});var r=function(e){var t=typeof e;return null!=e&&("object"==t||"function"==t)},i="object"==typeof global&&global&&global.Object===Object&&global,a="object"==typeof self&&self&&self.Object===Object&&self,o=i||a||Function("return this")(),s=function(){return o.Date.now()},c=/\s/;var l=function(e){for(var t=e.length;t--&&c.test(e.charAt(t)););return t},u=/^\s+/;var d=function(e){return e?e.slice(0,l(e)+1).replace(u,""):e},f=o.Symbol,m=Object.prototype,p=m.hasOwnProperty,h=m.toString,v=f?f.toStringTag:void 0;var x=function(e){var t=p.call(e,v),n=e[v];try{e[v]=void 0;var r=!0}catch(a){}var i=h.call(e);return r&&(t?e[v]=n:delete e[v]),i},b=Object.prototype.toString;var g=function(e){return b.call(e)},y=f?f.toStringTag:void 0;var j=function(e){return null==e?void 0===e?"[object Undefined]":"[object Null]":y&&y in Object(e)?x(e):g(e)};var Z=function(e){return null!=e&&"object"==typeof e};var w=function(e){return"symbol"==typeof e||Z(e)&&"[object Symbol]"==j(e)},N=/^[-+]0x[0-9a-f]+$/i,O=/^0b[01]+$/i,S=/^0o[0-7]+$/i,k=parseInt;var C=function(e){if("number"==typeof e)return e;if(w(e))return NaN;if(r(e)){var t="function"==typeof e.valueOf?e.valueOf():e;e=r(t)?t+"":t}if("string"!=typeof e)return 0===e?e:+e;e=d(e);var n=O.test(e);return n||S.test(e)?k(e.slice(2),n?2:8):N.test(e)?NaN:+e},I=Math.max,F=Math.min;var D=function(e,t,n){var i,a,o,c,l,u,d=0,f=!1,m=!1,p=!0;if("function"!=typeof e)throw new TypeError("Expected a function");function h(t){var n=i,r=a;return i=a=void 0,d=t,c=e.apply(r,n)}function v(e){return d=e,l=setTimeout(b,t),f?h(e):c}function x(e){var n=e-u;return void 0===u||n>=t||n<0||m&&e-d>=o}function b(){var e=s();if(x(e))return g(e);l=setTimeout(b,function(e){var n=t-(e-u);return m?F(n,o-(e-d)):n}(e))}function g(e){return l=void 0,p&&i?h(e):(i=a=void 0,c)}function y(){var e=s(),n=x(e);if(i=arguments,a=this,u=e,n){if(void 0===l)return v(u);if(m)return clearTimeout(l),l=setTimeout(b,t),h(u)}return void 0===l&&(l=setTimeout(b,t)),c}return t=C(t)||0,r(n)&&(f=!!n.leading,o=(m="maxWait"in n)?I(C(n.maxWait)||0,t):o,p="trailing"in n?!!n.trailing:p),y.cancel=function(){void 0!==l&&clearTimeout(l),d=0,i=u=a=l=void 0},y.flush=function(){return void 0===l?c:g(s())},y}}}]);
//# sourceMappingURL=267.2a3a7271.chunk.js.map