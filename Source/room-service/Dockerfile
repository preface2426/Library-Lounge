FROM node:24-alpine
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src ./src
ENV NODE_ENV=production
EXPOSE 8787
CMD ["node", "--import", "tsx", "src/server.ts"]
