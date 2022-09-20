"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[73],{7343:function(e,n,t){t.d(n,{Z:function(){return g}});var r=t(3504),a=t(4165),o=t(1413),i=t(5861),s=t(7408),c=t(1803),u=t(5754),l=t(6117),d=function(e){var n=e.dotYouId,t=e.originalContactData,r=(0,l.Z)().getSharedSecret,d=u.m.getInstance(r()),m=function(){var e=(0,i.Z)((0,a.Z)().mark((function e(n){var t,r,i,s,c;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(t=n.dotYouId,r=n.originalContactData,t){e.next=3;break}return e.abrupt("return");case 3:return e.next=5,d.getContactByDotYouId(t);case 5:if(!(i=e.sent)){e.next=9;break}return console.log("contact book",i),e.abrupt("return",p(i));case 9:if(!r){e.next=13;break}s={name:{givenName:r.givenName,surname:r.surname},image:r.image},e.next=17;break;case 13:return e.next=15,f(t);case 15:(c=e.sent)&&(s=c);case 17:if(!s){e.next=22;break}return e.next=20,d.saveContact((0,o.Z)((0,o.Z)({},s),{},{dotYouId:t}));case 20:return s=e.sent,e.abrupt("return",p(s));case 22:return e.abrupt("return",void 0);case 23:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),f=function(){var e=(0,i.Z)((0,a.Z)().mark((function e(n){var t,r,i,s,u,l,d,m,f,p,g,x,v;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return d=new c.pt({api:c.Ii.YouAuth,root:n}),e.next=3,d.fileReadOnlyProvider.GetFile("public.json");case 3:if(m=e.sent,f=null===(t=m.get("name"))||void 0===t?void 0:t[0],p=null===(r=m.get("photo"))||void 0===r?void 0:r[0],g=null!==(i=null===(s=m.get(null===p||void 0===p||null===(u=p.payload)||void 0===u||null===(l=u.data)||void 0===l?void 0:l.profileImageId))||void 0===s?void 0:s[0])&&void 0!==i?i:void 0){e.next=9;break}return e.abrupt("return",{name:f.payload.data.givenName||f.payload.data.surname?{givenName:f.payload.data.givenName,surname:f.payload.data.surname}:void 0});case 9:return x=g.additionalThumbnails.reduce((function(e,n){return e.pixelWidth<n.pixelWidth&&n.pixelWidth<=250?n:e}),(0,o.Z)((0,o.Z)({},g.header.fileMetadata.appData.previewThumbnail),{},{pixelWidth:20,pixelHeight:20})),v={name:f.payload.data.givenName||f.payload.data.surname?{givenName:f.payload.data.givenName,surname:f.payload.data.surname}:void 0,profileImage:{pixelWidth:x.pixelWidth,pixelHeight:x.pixelHeight,contentType:x.contentType,content:x.content}},e.abrupt("return",v);case 12:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),p=function(e){if(!e.image)return(0,o.Z)((0,o.Z)({},e),{},{image:void 0});var n=c.j4.base64ToUint8Array(e.image.content),t=window.URL.createObjectURL(new Blob([n]));return(0,o.Z)((0,o.Z)({},e),{},{image:(0,o.Z)((0,o.Z)({},e.image),{},{url:t})})};return{fetch:(0,s.useQuery)(["contact",n,!!t],(function(){return m({dotYouId:n,originalContactData:t})}),{refetchOnWindowFocus:!1,onError:function(e){return console.error(e)}})}},m=t(184),f=function(e){var n=e.initials;return(0,m.jsx)("div",{className:"flex aspect-square h-full w-full bg-slate-200 text-4xl font-light text-white dark:bg-slate-700 dark:text-black sm:text-6xl",children:(0,m.jsx)("span",{className:"m-auto uppercase",children:n})})},p=t(1191),g=function(e){var n,t,a=e.contactData,o=e.dotYouId,i=e.href,s=e.checked,c=e.className,u=e.children,l=e.onClick,g=function(e){var n=e.children;return i?(0,m.jsx)(r.rU,{to:i,children:n}):(0,m.jsx)(m.Fragment,{children:n})},x=d({dotYouId:o,originalContactData:a}).fetch,v=x.data;if(x.isLoading)return(0,m.jsx)(p.Z,{className:"aspect-[3/5] ".concat(c)});var h=null===v||void 0===v?void 0:v.name,b=h?"".concat(null!==(n=h.givenName)&&void 0!==n?n:""," ").concat(null!==(t=h.surname)&&void 0!==t?t:""):o,Z=null===v||void 0===v?void 0:v.image;return(0,m.jsx)("div",{className:c,children:(0,m.jsx)(g,{children:(0,m.jsxs)("div",{className:"h-full rounded-md border border-gray-200 border-opacity-60 bg-white transition-colors dark:border-gray-800 dark:bg-gray-800 ".concat(s?"border-4 border-indigo-500 dark:border-indigo-700":!1===s?"border-4":""),onClick:l,children:[(0,m.jsx)("div",{className:"aspect-square",children:Z?(0,m.jsx)("figure",{className:"relative overflow-hidden",children:(0,m.jsx)("img",{src:Z.url,width:Z.pixelWidth,height:Z.pixelHeight,className:"aspect-square w-full"})}):(0,m.jsx)(f,{initials:function(){var e,n;if(h)return(null===(e=h.givenName)||void 0===e?void 0:e[0])+(null===(n=h.surname)||void 0===n?void 0:n[0])+"";var t=null===o||void 0===o?void 0:o.split(".");return(null===t||void 0===t?void 0:t.length)>=2?t[0][0]+t[1][0]+"":"--"}()})}),(0,m.jsxs)("div",{className:"p-2",children:[(0,m.jsx)("h2",{className:"font-thiner mb-6 dark:text-white",children:null!==b&&void 0!==b?b:o}),u]})]})})})}},7585:function(e,n,t){var r=t(4165),a=t(5861),o=t(885),i=t(2791),s=t(4990),c=t(7903),u=t(7148),l=t(9072),d=t(8532),m=t(7343),f=t(184);n.Z=function(e){var n,t=e.senderDotYouId,p=e.contactData,g=e.children,x=e.className,v=(0,c.Z)({}),h=v.acceptRequest,b=(h.mutateAsync,h.status),Z=v.ignoreRequest,w=Z.mutate,y=Z.status,j=(0,u.Z)(),N=(0,i.useState)(!1),k=(0,o.Z)(N,2),C=k[0],I=k[1];return(0,f.jsxs)(f.Fragment,{children:[(0,f.jsxs)(m.Z,{className:x,contactData:p,dotYouId:t,href:null!==(n=t&&"/owner/connections/".concat(t))&&void 0!==n?n:void 0,children:[g,(0,f.jsxs)(l.Z,{type:"primary",className:"mb-2 w-full",onClick:function(e){return e.preventDefault(),I(!0),!1},state:b,children:[(0,s.t)("Confirm"),"..."]}),(0,f.jsx)(l.Z,{type:"secondary",className:"mb-2 w-full",onClick:function(e){return e.preventDefault(),w({senderDotYouId:t}),!1},state:y,children:(0,s.t)("Ignore")})]}),(0,f.jsx)(d.Z,{isOpen:C,senderDotYouId:t,title:(0,s.t)("Accept connection request"),confirmText:(0,s.t)("Accept and give access"),onConfirm:(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:j(),I(!1);case 2:case"end":return e.stop()}}),e)}))),onCancel:function(){j(),I(!1)}})]})}},6916:function(e,n,t){var r=t(1413),a=t(184);n.Z=function(e){var n;return(0,a.jsx)("input",(0,r.Z)((0,r.Z)({},e),{},{type:null!==(n=e.type)&&void 0!==n?n:"input",className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className)}))}},2465:function(e,n,t){var r=t(1413),a=t(184);n.Z=function(e){return(0,a.jsx)("textarea",(0,r.Z)((0,r.Z)({},e),{},{className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className)}))}},4661:function(e,n,t){var r=t(885),a=t(2791),o=t(4665),i=t(184);n.Z=function(e){var n=e.isOpenByDefault,t=void 0===n||n,s=e.title,c=e.className,u=e.children,l=e.isOpaqueBg,d=void 0!==l&&l,m=e.isBorderLess,f=void 0!==m&&m,p=(0,a.useState)(t),g=(0,r.Z)(p,2),x=g[0],v=g[1],h=(0,a.useRef)(null),b=(0,a.useRef)(0);return(0,a.useEffect)((function(){t&&h.current&&(b.current=h.current.clientHeight)}),[x]),(0,i.jsxs)("section",{className:"my-5 rounded-md ".concat(d?f?"":"rounded-lg border-[1px] border-gray-200 border-opacity-80 px-5 dark:border-gray-700":"bg-slate-50 px-5 dark:bg-slate-800 dark:text-slate-300"," ").concat(c),children:[(0,i.jsxs)("div",{className:"relative cursor-pointer border-b-[1px] border-slate-200 py-5 transition-all duration-300 ".concat(x?"border-opacity-100":"border-opacity-0"),onClick:function(){return v(!x)},children:[(0,i.jsx)("h3",{className:"text-2xl dark:text-white",children:s}),(0,i.jsx)("button",{className:"absolute top-0 right-0 bottom-0",children:(0,i.jsx)(o.Z,{className:"h-4 w-4 transition-transform duration-300 ".concat(x?"rotate-90":"-rotate-90")})})]}),(0,i.jsx)("div",{className:"overflow-hidden transition-all duration-300 ",style:{maxHeight:"".concat(x?b.current?b.current:2e3:0,"px")},ref:h,children:(0,i.jsx)("div",{className:"py-5 ",children:u})})]})}},8432:function(e,n,t){t.r(n),t.d(n,{default:function(){return S}});var r=t(885),a=t(3004),o=t(4257),i=t(9072),s=t(7585),c=t(7903),u=t(7343),l=t(184),d=function(e){var n,t=e.recipientDotYouId,r=e.className,a=(0,c.Z)({}).revokeConnectionRequest,o=a.mutate,s=a.status;return(0,l.jsx)(u.Z,{className:r,dotYouId:t,href:null!==(n=t&&"/owner/connections/".concat(t))&&void 0!==n?n:void 0,children:(0,l.jsx)(i.Z,{type:"secondary",className:"mb-2 w-full",onClick:function(e){return e.preventDefault(),o({targetDotYouId:t}),!1},state:s,children:"Cancel"})})},m=t(4990),f=function(e){var n,t=e.dotYouProfile,r=e.contactData,a=e.className,o=(0,c.Z)({}).disconnect,s=o.mutate,d=o.status;return(0,l.jsx)(u.Z,{className:a,contactData:r,dotYouId:t.dotYouId,href:null!==(n=t.dotYouId&&"/owner/connections/".concat(t.dotYouId))&&void 0!==n?n:void 0,children:(0,l.jsx)(i.Z,{type:"secondary",className:"w-full",onClick:function(e){e.preventDefault(),s({connectionDotYouId:t.dotYouId})},state:d,confirmOptions:{title:"".concat((0,m.t)("Remove")," ").concat(t.dotYouId),buttonText:(0,m.t)("Remove"),body:"".concat((0,m.t)("Are you sure you want to remove")," ").concat(t.dotYouId," ").concat((0,m.t)("from your connections. They will lose all existing access."))},children:"Remove"})})},p=t(8491),g=t(4661),x=t(2982),v=t(2791),h=t(4164),b=t(4395),Z=t(7148),w=t(3412),y=t(6916),j=t(4940),N=t(6234),k=t(2465),C=t(8808),I=t(6123),D=function(e){var n=e.title,t=e.isOpen,a=e.onConfirm,o=e.onCancel,s=(0,w.Z)("modal-container"),u=(0,c.Z)({}).sendConnectionRequest,d=u.mutate,f=u.status,p=u.reset,g=(0,b.Z)().fetch.data,D=(0,Z.Z)(),Y=(0,v.useState)("samwise.digital"),q=(0,r.Z)(Y,2),S=q[0],F=q[1],R=(0,v.useState)("Hi, I would like to connect with you"),O=(0,r.Z)(R,2),B=O[0],W=O[1],L=(0,v.useState)({givenName:"Frodo",surname:"Underhill"}),A=(0,r.Z)(L,2),T=A[0],H=A[1],P=(0,v.useState)(null),U=(0,r.Z)(P,2),Q=U[0],z=U[1],V=(0,v.useState)([]),E=(0,r.Z)(V,2),M=E[0],_=E[1];if(!t)return null;var G=(0,l.jsx)(I.Z,{title:n,onClose:o,children:(0,l.jsx)(l.Fragment,{children:(0,l.jsxs)("form",{onSubmit:function(e){e.preventDefault(),d({message:B,name:T,photoFileId:Q,targetDotYouId:S,circleIds:M},{onSuccess:function(){D(),p(),F(""),W(""),_([]),a()}})},children:[(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"dotyouid",children:"Recipient (dot you id)"}),(0,l.jsx)(y.Z,{id:"dotyouid",name:"dotyouid",onChange:function(e){F(e.target.value)},defaultValue:S,required:!0})]}),(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"dotyouid",children:"From:"}),(0,l.jsx)(j.Z,{id:"name",name:"name",defaultValue:"".concat(T.givenName,"+").concat(T.surname),required:!0,onChange:function(e){var n=e.target.value.split("+");H({givenName:n[0],surname:n[1]})}})]}),(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"dotyouid",children:"From:"}),(0,l.jsx)(N.Z,{id:"name",name:"name",onChange:function(e){console.log(e.target.value||void 0),z(e.target.value||void 0)}})]}),(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"message",children:(0,m.t)("Message")}),(0,l.jsx)(k.Z,{id:"message",name:"message",defaultValue:B,onChange:function(e){W(e.target.value)},required:!0})]}),g.length?(0,l.jsxs)(l.Fragment,{children:[(0,l.jsxs)("h2",{className:"mb-2 text-lg",children:[(0,m.t)("Add as member to one or more circles"),":"]}),g.map((function(e,n){var t;return(0,l.jsx)(C.Z,{circleDef:e,className:"my-4 w-full rounded-lg border p-4 ".concat(M.some((function(n){return n===e.id}))?"border-indigo-500 bg-slate-50 dark:border-indigo-700 dark:bg-slate-700":""),onClick:function(){var n=(0,x.Z)(M);n.some((function(n){return n===e.id}))?_(n.filter((function(n){return n!==e.id}))):(n.push(e.id),_(n))}},null!==(t=e.id)&&void 0!==t?t:n)}))]}):null,(0,l.jsxs)("div",{className:"-m-2 flex flex-row-reverse py-3",children:[(0,l.jsx)(i.Z,{className:"m-2",state:f,icon:"send",children:(0,m.t)("Send")}),(0,l.jsx)(i.Z,{className:"m-2",type:"secondary",onClick:o,children:(0,m.t)("Cancel")})]})]})})});return(0,h.createPortal)(G,s)},Y=t(959),q=t(1191),S=function(){var e=(0,o.Z)(),n=e.fetchPending,t=n.data,c=n.isLoading,u=e.fetchActive,x=u.data,h=u.isLoading,b=e.fetchSent,Z=b.data,w=b.isLoading,y=(0,v.useState)(!1),j=(0,r.Z)(y,2),N=j[0],k=j[1];return(0,l.jsxs)(l.Fragment,{children:[(0,l.jsxs)("section",{children:[(0,l.jsx)(a.Z,{icon:Y.Z,title:"Connections",actions:(0,l.jsx)(l.Fragment,{children:(0,l.jsx)(i.Z,{onClick:function(){return k(!0)},icon:"plus",className:"m-2",children:(0,m.t)("Send request")})})}),!c&&null!==t&&void 0!==t&&t.length?(0,l.jsx)(p.Z,{isOpaqueBg:!0,isBorderLess:!0,title:(0,m.t)("Connection requests"),children:(0,l.jsx)("div",{className:"-m-1 flex flex-row flex-wrap",children:null===t||void 0===t?void 0:t.map((function(e){return(0,l.jsx)(s.Z,{className:"w-1/2 p-1 sm:w-1/2 md:w-1/3 lg:w-1/4 xl:w-1/6",senderDotYouId:e.senderDotYouId,contactData:e.contactData,children:(0,l.jsx)("div",{className:"-mt-3",children:(0,l.jsx)("p",{className:"mb-3 text-sm",children:e.message})})},e.senderDotYouId)}))})}):null,null!==x&&void 0!==x&&x.length||h?(0,l.jsx)(p.Z,{isOpaqueBg:!0,isBorderLess:!0,title:(0,m.t)("Your Connections"),children:(0,l.jsxs)("div",{className:"-m-1 flex flex-row flex-wrap",children:[h&&(0,l.jsxs)(l.Fragment,{children:[(0,l.jsx)(q.Z,{className:"m-1 aspect-square w-1/2 sm:w-1/2 md:w-1/3 lg:w-1/4 xl:w-1/6"}),(0,l.jsx)(q.Z,{className:"m-1 aspect-square w-1/2 sm:w-1/2 md:w-1/3 lg:w-1/4 xl:w-1/6"})]}),null===x||void 0===x?void 0:x.map((function(e){return(0,l.jsx)(f,{className:"w-1/2 p-1 sm:w-1/2 md:w-1/3 lg:w-1/4 xl:w-1/6",dotYouProfile:e,contactData:e.originalContactData},e.dotYouId)}))]})}):null,!w&&null!==Z&&void 0!==Z&&Z.length?(0,l.jsx)(g.Z,{isOpaqueBg:!0,isBorderLess:!0,isOpenByDefault:!0,title:(0,m.t)("Sent Connection Requests"),children:(0,l.jsx)("div",{className:"-m-1 flex flex-row flex-wrap",children:null===Z||void 0===Z?void 0:Z.map((function(e){return(0,l.jsx)(d,{className:"w-1/2 p-1 sm:w-1/2 md:w-1/3 lg:w-1/4 xl:w-1/6",recipientDotYouId:e.recipient},e.recipient)}))})}):null]}),(0,l.jsx)(D,{title:(0,m.t)("Send connection request"),isOpen:N,onConfirm:function(){return k(!1)},onCancel:function(){return k(!1)}})]})}},4257:function(e,n,t){var r=t(4165),a=t(5861),o=t(7408),i=t(7186),s=t(5144),c=t(6117);n.Z=function(){var e=(0,c.Z)().getSharedSecret,n=s.e.getInstance(e()),t=i.I.getInstance(e()),u=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,n.getPendingRequests({pageNumber:1,pageSize:10});case 2:return e.next=4,e.sent.results;case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),l=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,n.getSentRequests({pageNumber:1,pageSize:10});case 2:return e.next=4,e.sent.results;case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),d=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.getConnections({pageNumber:1,pageSize:10});case 2:return e.next=4,e.sent.results;case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return{fetchPending:(0,o.useQuery)(["pendingConnections"],(function(){return u()}),{refetchOnWindowFocus:!1}),fetchSent:(0,o.useQuery)(["sentRequests"],(function(){return l()}),{refetchOnWindowFocus:!1}),fetchActive:(0,o.useQuery)(["activeConnections"],(function(){return d()}),{refetchOnWindowFocus:!1})}}},7148:function(e,n,t){var r=t(885),a=t(3504);n.Z=function(){var e=(0,a.lr)(),n=(0,r.Z)(e,1)[0];return function(){if("focus"===n.get("ui")||"minimal"===n.get("ui")){var e=n.get("return");window.location.href=e}}}}}]);
//# sourceMappingURL=73.07f4fc89.chunk.js.map