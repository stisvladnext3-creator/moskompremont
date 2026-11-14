include $(TOPDIR)/rules.mk
PKG_NAME:=mkr-router-updater
PKG_VERSION:=0.1.0
PKG_RELEASE:=1
PKG_LICENSE:=GPL-2.0-or-later
PKG_MAINTAINER:=MosKomRemont
PKGARCH:=all
MKR_SOURCE_DIR:=$(abspath $(CURDIR)/../..)
include $(INCLUDE_DIR)/package.mk

define Package/mkr-router-updater
  SECTION:=net
  CATEGORY:=Network
  TITLE:=MosKomRemont Podkop connection updater
  DEPENDS:=+curl +jq +uci +ca-bundle +flock +coreutils-base64
endef

define Package/mkr-router-updater/conffiles
/etc/config/mkr_router
endef

define Build/Compile
endef

define Package/mkr-router-updater/install
	$(CP) $(MKR_SOURCE_DIR)/files/* $(1)/
	chmod 600 $(1)/etc/config/mkr_router
	chmod 755 $(1)/usr/bin/mkr-router $(1)/usr/libexec/rpcd/mkr-router $(1)/etc/init.d/mkr_router
endef

define Package/mkr-router-updater/postinst
#!/bin/sh
[ -n "$$IPKG_INSTROOT" ] && exit 0
/etc/init.d/mkr_router enable >/dev/null 2>&1 || :
/etc/init.d/mkr_router restart >/dev/null 2>&1 || :
exit 0
endef

$(eval $(call BuildPackage,mkr-router-updater))
