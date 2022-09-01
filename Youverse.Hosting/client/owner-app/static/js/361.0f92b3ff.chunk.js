"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[361],{6064:function(e,n,t){t.r(n),t.d(n,{default:function(){return k}});var i=t(1413),r=t(2982),o=t(885),a=t(2791),s=t(6871),c=t(7221),u=t(3431),l=t(184),d=function(e){var n=e.className,t=e.items,i=e.onChange;return(0,l.jsx)("div",{className:"flex ".concat(n),children:t.map((function(e){var n;return(0,l.jsx)("a",{className:"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(e.isActive?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(n=e.className)&&void 0!==n?n:""),onClick:function(){i(e.key)},children:e.title},e.key)}))})},f=t(1803),p=t(4990),m=t(9077),h=t(8191),x=t(3225),v=t(8491),g=t(4226),j=function(e){var n,t=e.profileId,r=e.sectionId,s=(0,a.useState)(!1),c=(0,o.Z)(s,2),u=c[0],d=c[1],j=(0,a.useState)(),y=(0,o.Z)(j,2),Z=y[0],b=y[1],w=(0,m.Z)({}),I=(0,o.Z)(w,2)[1],N=I.mutate,k=I.isLoading,S=I.isError,C=I.isSuccess,O=function(){d(!1),b(void 0)};return(0,l.jsx)(l.Fragment,{children:u?(0,l.jsxs)(v.Z,{title:"New".concat(Z?":":""," ").concat(null!==(n=null===Z||void 0===Z?void 0:Z.typeDefinition.name)&&void 0!==n?n:""),isOpaqueBg:!0,children:[void 0===Z?(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"type",children:(0,p.t)("Attribute Type")}),(0,l.jsxs)(x.Z,{id:"type",onChange:function(e){!function(e){var n=Object.values(f.H1).find((function(n){return n.type.toString()===e}));b({id:"",type:e,sectionId:r,priority:-1,data:{},typeDefinition:n,profileId:t,acl:{requiredSecurityGroup:f.hh.Owner}})}(e.target.value)},children:[(0,l.jsx)("option",{children:(0,p.t)("Make a selection")}),Object.values(f.H1).map((function(e){return(0,l.jsx)("option",{value:e.type.toString(),children:e.name},e.type.toString())}))]})]}):(0,l.jsx)(g.Z,{attribute:Z,onChange:function(e){if(Z){var n=(0,i.Z)({},Z);n.data[e.target.name]=e.target.value,b(n)}}}),(0,l.jsxs)("div",{className:"flex flex-row",children:[(0,l.jsx)(h.Z,{type:"secondary",className:"ml-auto",onClick:O,children:(0,p.t)("Cancel")}),(0,l.jsx)(h.Z,{type:"primary",icon:"plus",className:"ml-2",onClick:function(){console.log(Z),N(Z,{onSuccess:function(){O()}})},state:k?"loading":C?"success":S?"failed":void 0,children:(0,p.t)("Add")})]})]}):(0,l.jsx)("div",{className:"flex flex-row",children:(0,l.jsx)(h.Z,{type:"primary",icon:"plus",className:"mx-auto min-w-[9rem]",onClick:function(){return d(!0)},children:(0,p.t)("Add Attribute")})})})},y=t(6916),Z=t(5207),b=t(3004),w=t(2864),I=function(e){var n=e.profileDefinition,t=(0,a.useState)(""),s=(0,o.Z)(t,2),c=s[0],u=s[1];return(0,l.jsx)(v.Z,{title:"New: section",isOpaqueBg:!0,children:(0,l.jsxs)("form",{onSubmit:function(e){e.preventDefault();var t={sectionId:"",attributes:[],priority:Math.max.apply(Math,(0,r.Z)(n.sections.map((function(e){return e.priority}))))+1,isSystemSection:!1,name:c},o=(0,i.Z)({},n);return o.sections.push(t),console.log("Should create: ",o),!1},children:[(0,l.jsxs)("div",{className:"mb-5",children:[(0,l.jsx)("label",{htmlFor:"name",children:(0,p.t)("Name")}),(0,l.jsx)(y.Z,{id:"name",name:"sectionName",onChange:function(e){u(e.target.value)},required:!0})]}),(0,l.jsx)("div",{className:"flex flex-row",children:(0,l.jsx)(h.Z,{className:"ml-auto",children:(0,p.t)("add section")})})]})})},N=function(e){var n=e.section,t=e.profileId,i=(0,c.Z)({profileId:t,sectionId:n.sectionId}),a=(0,o.Z)(i,1)[0],s=a.data,u=a.isLoading;if(!s||u)return(0,l.jsx)(l.Fragment,{children:"Loading"});var d=s.reduce((function(e,n){return-1!==e.indexOf(n.type)?e:[].concat((0,r.Z)(e),[n.type])}),[]).map((function(e){var n=s.filter((function(n){return n.type===e}));return{name:n[0].typeDefinition.name,attributes:n}}));return(0,l.jsxs)(l.Fragment,{children:[s.length?d.map((function(e){return(0,l.jsx)(w.Z,{groupTitle:e.name,attributes:e.attributes},e.name)})):(0,l.jsx)("div",{className:"py-5",children:(0,p.t)("section-empty-attributes")}),(0,l.jsx)(j,{profileId:t,sectionId:n.sectionId})]})},k=function(){var e=(0,u.Z)(),n=e.data,t=e.isLoading,i=(0,s.UO)().profileKey,c=null===n||void 0===n?void 0:n.definitions.find((function(e){return e.slug===i})),f=(0,a.useState)(null!==c&&void 0!==c&&c.sections?c.sections[0].sectionId:""),m=(0,o.Z)(f,2),h=m[0],x=m[1];if(t)return(0,l.jsx)(l.Fragment,{children:"Loading"});if(!n)return(0,l.jsx)(l.Fragment,{children:(0,p.t)("no-data-found")});if(!c)return(0,l.jsx)(l.Fragment,{children:"Incorrect profile path"});var v="new"===h?void 0:c.sections.find((function(e){return e.sectionId===h}))||c.sections[0];return(0,l.jsxs)(l.Fragment,{children:[(0,l.jsx)(b.Z,{title:c.name}),(0,l.jsx)(d,{className:"mt-5",items:[].concat((0,r.Z)(c.sections.map((function(e,n){return{title:e.name,key:e.sectionId,isActive:h?h===e.sectionId:0===n}}))),[{title:(0,l.jsx)(Z.Z,{className:"h-5 w-5"}),key:"new",isActive:h?"new"===h:!c.sections.length,className:"flex-grow-0"}]),onChange:function(e){x(e)}}),"new"===h?(0,l.jsx)(I,{profileDefinition:c}):v&&(0,l.jsx)(N,{section:v,profileId:c.profileId},v.sectionId)]})}},3990:function(e,n,t){t.d(n,{P:function(){return r},V:function(){return i}});var i=function(e){return e.split(" ").join("-").toLowerCase()},r=function(e){return Object.keys(e).map((function(n){return n+"="+e[n]})).join("&")}},3431:function(e,n,t){var i=t(4165),r=t(1413),o=t(5861),a=t(7408),s=t(1803),c=t(3990),u=t(6117);n.Z=function(){var e=(0,u.Z)().getSharedSecret,n=function(){var n=(0,o.Z)((0,i.Z)().mark((function n(){var t,o;return(0,i.Z)().wrap((function(n){for(;;)switch(n.prev=n.next){case 0:return t=new s.KU({api:s.Ii.Owner,sharedSecret:e()}),n.next=3,t.profileDefinitionProvider.getProfileDefinitions();case 3:return n.next=5,n.sent.map((function(e){return(0,r.Z)((0,r.Z)({},e),{},{slug:(0,c.V)(e.name)})}));case 5:return o=n.sent,n.abrupt("return",{definitions:o});case 7:case"end":return n.stop()}}),n)})));return function(){return n.apply(this,arguments)}}();return(0,a.useQuery)(["profiles"],(function(){return n()}),{refetchOnMount:!1,refetchOnWindowFocus:!1})}}}]);
//# sourceMappingURL=361.0f92b3ff.chunk.js.map