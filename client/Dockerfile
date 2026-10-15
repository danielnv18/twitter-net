FROM node:24-alpine

WORKDIR /app
COPY . .
EXPOSE 5173

# A future Vue/Vite project should provide the standard npm dev script.
CMD ["sh", "-c", "if [ -f package.json ]; then npm install && exec npm run dev -- --host 0.0.0.0 --port 5173 --strictPort; else echo 'Client placeholder: add a Vue/Vite project in client/, then restart this service.'; exec sleep infinity; fi"]
